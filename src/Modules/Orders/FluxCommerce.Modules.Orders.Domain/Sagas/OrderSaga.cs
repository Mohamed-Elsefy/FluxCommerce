using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Results;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.Sagas;

/// <summary>
/// Phase 8.2 persistent state representing the progress of the Order/Inventory Saga (plan §7). It
/// stores only workflow progress (identity, correlation, state, timestamps, concurrency version) and
/// never duplicates Order or Inventory business state. Transitions are validated by the state machine:
/// <c>Started --RequestReservation--&gt; WaitingForInventory --Complete/Fail--&gt; Completed/Failed</c>.
/// Duplicate requests/results on the target state are idempotent no-ops (safe under at-least-once
/// delivery); any other transition is rejected. The <see cref="Version"/> is an optimistic concurrency
/// token, so conflicting concurrent results cannot be applied twice at the database.
///
/// <para>
/// Phase 8.7 adds <see cref="ReservationIds"/> — the identifiers of the reservations the Inventory
/// module created for this order, recorded when <c>inventory.reserved</c> is applied. They drive the
/// compensation release: when the order is cancelled the orchestrator issues one
/// <c>inventory.release_requested</c> command per recorded reservation (ADR-009). The identifiers are
/// stored as a JSON primitive collection; they are an append-only history, never a workflow state, so
/// recording them does not advance the saga state machine or bump the concurrency version.
/// </para>
///
/// <para>
/// Phase 8.8 adds <see cref="ReleaseRequestedAtUtc"/> — the moment the compensation release was
/// requested for this order (bookkeeping, set by the orchestrator when <c>orders.order_cancelled</c> is
/// processed). It is not a workflow state: marking it never advances the state machine or bumps the
/// concurrency version. It lets the saga detect a late <c>inventory.reserved</c> result that races the
/// cancellation and arrives AFTER the release was issued, so the orchestrator can compensate that
/// reservation immediately instead of letting it leak until Inventory expiry (Phase 11).
/// </para>
///
/// <para>
/// Phase 10.2 extends the state machine to include Payment and compensation (plan §10.2).
/// New states: <c>WaitingForPayment</c> (after inventory reserved), <c>ReleasingInventory</c>
/// (after payment failed), <c>Cancelled</c> (OrderCancelled after compensation). Payment
/// tracking properties <see cref="PaymentId"/>, <see cref="ProviderPaymentIntentId"/>,
/// <see cref="PaymentFailureReason"/>, <see cref="PaymentRequestedAtUtc"/> are bookkeeping
/// (first write wins, no version bump). Valid transitions are explicit:
/// <c>WaitingForInventory → WaitingForPayment → Completed</c> (payment succeeded) and
/// <c>WaitingForPayment → ReleasingInventory → Cancelled → Completed</c> (payment failed).
/// Duplicate events on the target state are idempotent no-ops; invalid/out-of-order events
/// are rejected with <see cref="OrderSagaErrors.InvalidStateTransition"/>.
/// </para>
/// </summary>
public sealed class OrderSaga
{
    public const long InitialVersion = 1;

    public Guid Id { get; }

    public OrderId OrderId { get; }

    public Guid CorrelationId { get; }

    public OrderSagaState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc => _updatedAtUtc;

    public long Version => _version;

    /// <summary>
    /// Phase 8.7 the reservations the Inventory module created for this order, in arrival order. The
    /// collection is append-only: recording is idempotent and never advances the saga state machine.
    /// </summary>
    public IReadOnlyCollection<Guid> ReservationIds => _reservationIds;

    /// <summary>
    /// Phase 8.8 the moment the compensation release was requested for this order, or <c>null</c> when
    /// no release has been requested. Bookkeeping only: marking it never advances the state machine or
    /// bumps the concurrency version. It distinguishes a late <c>inventory.reserved</c> result that
    /// must still be compensated from one that was recorded before the release was issued.
    /// </summary>
    public DateTimeOffset? ReleaseRequestedAtUtc => _releaseRequestedAtUtc;

    /// <summary>
    /// Phase 10.2 payment tracking — the payment associated with this saga, if any.
    /// Null until the saga enters <see cref="OrderSagaState.WaitingForPayment"/> and
    /// a payment intent is requested.
    /// </summary>
    public Guid? PaymentId => _paymentId;

    /// <summary>
    /// Phase 10.2 provider-side payment intent identifier (Stripe <c>pi_*</c>), if known.
    /// </summary>
    public string? ProviderPaymentIntentId => _providerPaymentIntentId;

    /// <summary>
    /// Phase 10.2 failure reason when <see cref="FailPayment"/> transitions to
    /// <see cref="OrderSagaState.ReleasingInventory"/>.
    /// </summary>
    public string? PaymentFailureReason => _paymentFailureReason;

    /// <summary>
    /// Phase 10.2 moment the payment was requested for this order, or <c>null</c> when
    /// no payment has been requested. Bookkeeping only — does not bump the concurrency version.
    /// </summary>
    public DateTimeOffset? PaymentRequestedAtUtc => _paymentRequestedAtUtc;

    private List<Guid> _reservationIds = [];

    private DateTimeOffset? _releaseRequestedAtUtc;

    private Guid? _paymentId;

    private string? _providerPaymentIntentId;

    private string? _paymentFailureReason;

    private DateTimeOffset? _paymentRequestedAtUtc;

    private long _version;

    private DateTimeOffset _updatedAtUtc;

    private OrderSaga(
        Guid id,
        OrderId orderId,
        Guid correlationId,
        OrderSagaState state,
        long version,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrderId = orderId;
        CorrelationId = correlationId;
        State = state;
        _version = version;
        CreatedAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
    }

    #pragma warning disable CS8618
    private OrderSaga() { }
    #pragma warning restore CS8618

    public static OrderSaga Start(
        Guid id,
        OrderId orderId,
        Guid correlationId,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A saga id must not be an empty GUID.", nameof(id));

        if (orderId is null)
            throw new ArgumentNullException(nameof(orderId));

        if (utcNow == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(utcNow));

        return new OrderSaga(id, orderId, correlationId, OrderSagaState.Started, InitialVersion, utcNow);
    }

    /// <summary>
    /// Records that the reservation request was issued: <c>Started --&gt; WaitingForInventory</c>. A
    /// saga already waiting for inventory is an idempotent no-op (the request may have been issued and
    /// the confirmation lost); a terminal saga rejects the request.
    /// </summary>
    public DomainResult RequestReservation(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.Started => Transition(OrderSagaState.WaitingForInventory, utcNow),
            OrderSagaState.WaitingForInventory => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Applies a successful reservation: <c>WaitingForInventory --&gt; Completed</c>. A duplicate result
    /// on an already-completed saga is an idempotent no-op; any other state is rejected.
    /// </summary>
    public DomainResult Complete(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.WaitingForInventory => Transition(OrderSagaState.Completed, utcNow),
            OrderSagaState.Completed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Applies a failed reservation: <c>WaitingForInventory --&gt; Failed</c>. A duplicate result on an
    /// already-failed saga is an idempotent no-op; a conflicting result on a completed saga is rejected.
    /// </summary>
    public DomainResult Fail(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.WaitingForInventory => Transition(OrderSagaState.Failed, utcNow),
            OrderSagaState.Failed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Phase 8.7 records a reservation the Inventory module created for this order. A reservation id
    /// already recorded is an idempotent no-op (the same <c>inventory.reserved</c> result can be
    /// delivered more than once, and a multi-line order can report its line reservations in any order).
    /// Recording never advances the saga state machine or bumps the concurrency version — the recorded
    /// identifiers are an append-only history that drives the compensation release issued when the
    /// order is cancelled. Returns <c>true</c> when the identifier was newly recorded.
    /// </summary>
    public bool RecordReservation(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
            throw new ArgumentException("A reservation id must not be an empty GUID.", nameof(reservationId));

        if (_reservationIds.Contains(reservationId))
            return false;

        _reservationIds.Add(reservationId);
        return true;
    }

    /// <summary>
    /// Phase 8.8 records that the compensation release was requested for this order. The marker is
    /// bookkeeping, not a workflow state: it never advances the saga state machine and never bumps the
    /// concurrency version (multiple cancellations / redeliveries all observe the same release request).
    /// The first moment is kept, so a redelivered <c>orders.order_cancelled</c> cannot shift it.
    /// </summary>
    public void MarkReleaseRequested(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        _releaseRequestedAtUtc ??= utcNow;
    }

    // ---- Phase 10.2 Payment state machine ----

    /// <summary>
    /// Phase 10.2 transitions <c>WaitingForInventory → WaitingForPayment</c> after all
    /// <c>inventory.reserved</c> results have been applied. A saga already waiting for
    /// payment is an idempotent no-op (duplicate reservation result); any other state
    /// is rejected.
    /// </summary>
    public DomainResult MarkWaitingForPayment(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.WaitingForInventory => Transition(OrderSagaState.WaitingForPayment, utcNow),
            OrderSagaState.WaitingForPayment => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Phase 10.2 records the payment intent associated with this saga. Bookkeeping only:
    /// it never advances the state machine or bumps the concurrency version. Only valid
    /// in <c>WaitingForPayment</c>; duplicate calls with the same <paramref name="paymentId"/>
    /// are idempotent no-ops; a different payment id on an already-recorded saga is rejected
    /// to prevent cross-payment contamination.
    /// </summary>
    public DomainResult MarkPaymentRequested(Guid paymentId, string? providerPaymentIntentId, DateTimeOffset utcNow)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));

        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (State != OrderSagaState.WaitingForPayment)
            return DomainResult.Failure(OrderSagaErrors.InvalidStateTransition);

        if (_paymentId.HasValue)
        {
            if (_paymentId.Value == paymentId)
                return DomainResult.Success();

            return DomainResult.Failure(OrderSagaErrors.InvalidStateTransition);
        }

        _paymentId = paymentId;
        if (!string.IsNullOrWhiteSpace(providerPaymentIntentId))
            _providerPaymentIntentId = providerPaymentIntentId;
        _paymentRequestedAtUtc ??= utcNow;

        return DomainResult.Success();
    }

    /// <summary>
    /// Phase 10.2 applies <c>payments.payment_succeeded</c>:
    /// <c>WaitingForPayment → Completed</c>. A duplicate on an already-completed saga
    /// is an idempotent no-op; a result on <c>ReleasingInventory</c>/<c>Cancelled</c>/
    /// <c>Failed</c> is a conflicting rejection; any other state is rejected.
    /// </summary>
    public DomainResult SucceedPayment(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.WaitingForPayment => Transition(OrderSagaState.Completed, utcNow),
            OrderSagaState.Completed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Phase 10.2 applies <c>payments.payment_failed</c>:
    /// <c>WaitingForPayment → ReleasingInventory</c>. The <paramref name="failureReason"/>
    /// is recorded. A duplicate on <c>ReleasingInventory</c> or <c>Cancelled</c> is an
    /// idempotent no-op (single compensation decision under at-least-once delivery);
    /// a duplicate on <c>Completed</c> is a conflicting rejection (already succeeded);
    /// any other state is rejected.
    /// </summary>
    public DomainResult FailPayment(DateTimeOffset utcNow, string failureReason)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("A failure reason is required.", nameof(failureReason));

        return State switch
        {
            OrderSagaState.WaitingForPayment => FailPaymentTransition(utcNow, failureReason),
            OrderSagaState.ReleasingInventory => DomainResult.Success(),
            OrderSagaState.Cancelled => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    private DomainResult FailPaymentTransition(DateTimeOffset utcNow, string failureReason)
    {
        _paymentFailureReason = failureReason;
        return Transition(OrderSagaState.ReleasingInventory, utcNow);
    }

    /// <summary>
    /// Phase 10.2 completes compensation after inventory was released:
    /// <c>ReleasingInventory → Cancelled</c> (OrderCancelled). A duplicate on
    /// <c>Cancelled</c> or <c>Completed</c> is an idempotent no-op; any other
    /// state is rejected.
    /// </summary>
    public DomainResult MarkInventoryReleased(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.ReleasingInventory => Transition(OrderSagaState.Cancelled, utcNow),
            OrderSagaState.Cancelled => DomainResult.Success(),
            OrderSagaState.Completed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    /// <summary>
    /// Phase 10.2 finalizes the cancelled saga to <c>Completed</c> for E2E traceability:
    /// <c>Cancelled → Completed</c>. A duplicate on <c>Completed</c> is an idempotent
    /// no-op; any other state is rejected.
    /// </summary>
    public DomainResult FinalizeCancellation(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return State switch
        {
            OrderSagaState.Cancelled => Transition(OrderSagaState.Completed, utcNow),
            OrderSagaState.Completed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderSagaErrors.InvalidStateTransition)
        };
    }

    public static OrderSaga Hydrate(
        Guid id,
        OrderId orderId,
        Guid correlationId,
        OrderSagaState state,
        long version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? releaseRequestedAtUtc = null,
        Guid? paymentId = null,
        string? providerPaymentIntentId = null,
        string? paymentFailureReason = null,
        DateTimeOffset? paymentRequestedAtUtc = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A saga id must not be an empty GUID.", nameof(id));

        if (orderId is null)
            throw new ArgumentNullException(nameof(orderId));

        if (version < InitialVersion)
            throw new ArgumentException(
                $"A saga version must be at least {InitialVersion}.",
                nameof(version));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        if (updatedAtUtc == default)
            throw new ArgumentException("A valid update timestamp is required.", nameof(updatedAtUtc));

        if (updatedAtUtc < createdAtUtc)
            throw new ArgumentException(
                "The update timestamp must not precede the creation timestamp.",
                nameof(updatedAtUtc));

        if (releaseRequestedAtUtc is not null && releaseRequestedAtUtc.Value == default)
            throw new ArgumentException(
                "A valid release-requested timestamp is required.",
                nameof(releaseRequestedAtUtc));

        if (paymentId.HasValue && paymentId.Value == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));

        if (paymentRequestedAtUtc is not null && paymentRequestedAtUtc.Value == default)
            throw new ArgumentException(
                "A valid payment-requested timestamp is required.",
                nameof(paymentRequestedAtUtc));

        var saga = new OrderSaga(id, orderId, correlationId, state, version, createdAtUtc);
        saga._updatedAtUtc = updatedAtUtc;
        saga._releaseRequestedAtUtc = releaseRequestedAtUtc;
        saga._paymentId = paymentId;
        saga._providerPaymentIntentId = providerPaymentIntentId;
        saga._paymentFailureReason = paymentFailureReason;
        saga._paymentRequestedAtUtc = paymentRequestedAtUtc;

        return saga;
    }

    private DomainResult Transition(OrderSagaState nextState, DateTimeOffset utcNow)
    {
        State = nextState;
        Touch(utcNow);

        return DomainResult.Success();
    }

    private void Touch(DateTimeOffset utcNow)
    {
        _version++;
        _updatedAtUtc = utcNow;
    }
}