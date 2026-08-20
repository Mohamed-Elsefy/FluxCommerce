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

    private List<Guid> _reservationIds = [];

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

    public static OrderSaga Hydrate(
        Guid id,
        OrderId orderId,
        Guid correlationId,
        OrderSagaState state,
        long version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
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

        var saga = new OrderSaga(id, orderId, correlationId, state, version, createdAtUtc);
        saga._updatedAtUtc = updatedAtUtc;

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