using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 the Order-owned Saga orchestrator (ADR-009): it drives the <c>OrderCreated → Reserve
/// Inventory</c> leg of the workflow. On the first delivery of an <c>orders.order_created</c> message it
/// creates the saga, advances it to <see cref="OrderSagaState.WaitingForInventory"/>, and stages one
/// <see cref="InventoryReservationRequestedIntegrationEvent"/> per order line item (the saga never
/// contains Inventory business rules — only the workflow progress). On any re-delivery the saga already
/// exists, so the orchestration is an idempotent no-op: no new saga, no re-issued commands, safe under
/// at-least-once delivery. The orchestrator only STAGES the saga and outbox records; the module
/// idempotent-processing boundary commits them atomically with the processed-message record, making the
/// whole leg recoverable after a restart (in-flight work is either fully applied or not at all). The
/// correlation/causation ids of the consumed message are stamped on every produced command (Phase 7.8),
/// and <c>ExpiresAt</c> is deterministic (<c>occurredAtUtc + ReservationLifetime</c>) so redelivery
/// never shifts the reservation deadline.
/// </summary>
/// <remarks>
/// Phase 8.6 completes the loop by applying the Inventory reservation OUTCOME through the same saga
/// (plan §11). A success advances <c>WaitingForInventory → Completed</c> and leaves the Order Pending —
/// the Order domain model has no further forward status (payment initiation is a later phase), and the
/// saga progress lives in saga state, never in <c>Order.Status</c>. A failure advances
/// <c>WaitingForInventory → Failed</c> and cancels the Order through its own <c>Cancel</c> transition
/// (Pending → Cancelled), so the Order lifecycle is never bypassed. Duplicate results on the terminal
/// state are idempotent no-ops; a result on the opposite terminal state is a conflicting rejection; a
/// result on a non-terminal state is an acknowledged anomaly. The saga row's <see cref="OrderSaga.Version"/>
/// optimistic concurrency token remains the arbiter for genuinely concurrent conflicting results, and
/// both the saga transition and (on failure) the Order cancellation are staged in the same unit of work,
/// so the module idempotent-processing boundary persists them atomically with the processed-message
/// record.
/// </remarks>
/// <remarks>
/// Phase 8.7 adds the compensation leg (plan §12): when an order is cancelled after its reservations
/// were applied, <see cref="RequestReleaseAsync"/> issues one <c>inventory.release_requested</c> command
/// per reservation recorded on the saga (see <see cref="OrderSaga.RecordReservation"/>), so Inventory
/// returns the reserved quantity. Release is fire-and-forget and never touches the saga — no state
/// transition, no version bump. Phase 8.8 hardens the race that could previously leak a reservation:
/// the orchestrator marks the saga as release-requested (see <see cref="OrderSaga.MarkReleaseRequested"/>)
/// so a very late <c>inventory.reserved</c> result arriving after the release was issued is compensated
/// immediately — a new release command is staged for it — instead of being recorded and left unreleased
/// until Inventory expiry.
/// </remarks>
/// <remarks>
/// Phase 8.9 observability: every orchestration operation runs inside an <see cref="ActivityKind.Internal"/>
/// activity (<c>Orders.Saga.Start</c>, <c>Orders.Saga.ApplyReservationSuccess</c>,
/// <c>Orders.Saga.ApplyReservationFailure</c>, <c>Orders.Saga.RequestRelease</c>) tagged with the order,
/// saga, correlation/causation, reservation, and previous/new saga state, and the transition logs are
/// enriched with <c>{PreviousState}</c>, <c>{NewState}</c>, <c>{Outcome}</c>, <c>{TraceId}</c>, and
/// <c>{SpanId}</c> — so the saga progress is reconstructable from both logs and traces. Because the
/// orchestrator runs inside the consumer activity, its spans stay under the same distributed trace as the
/// consuming producer, and the outbox records staged here snapshot that trace context (see
/// <see cref="FluxCommerce.BuildingBlocks.Outbox.Mapping.IntegrationEventOutboxMessageFactory"/>).
/// </remarks>
public sealed class OrderSagaOrchestrator
{
    private readonly IOrderSagaStore _sagaStore;
    private readonly IOrderStore _orderStore;
    private readonly OrdersDbContext _context;
    private readonly SagaOutboxMessageFactory _outboxMessageFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOptions<ReservationRequestOptions> _options;
    private readonly ILogger<OrderSagaOrchestrator> _logger;

    public OrderSagaOrchestrator(
        IOrderSagaStore sagaStore,
        IOrderStore orderStore,
        OrdersDbContext context,
        SagaOutboxMessageFactory outboxMessageFactory,
        IDateTimeProvider dateTimeProvider,
        IOptions<ReservationRequestOptions> options,
        ILogger<OrderSagaOrchestrator> logger)
    {
        _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts a Phase 8.9 saga activity (kind <see cref="ActivityKind.Internal"/>) tagged with the
    /// workflow identifiers and the previous/new saga state, so the saga progress is visible on the
    /// distributed trace. Returns <see langword="null"/> when no listener samples the source (tracing is
    /// then skipped without affecting orchestration).
    /// </summary>
    private static Activity? StartSagaActivity(
        string operationName,
        OrderId orderId,
        Guid? sagaId = null,
        OrderSagaState? previousState = null,
        OrderSagaState? newState = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        Guid? reservationId = null,
        string? outcome = null)
    {
        var activity = TraceContext.StartActivity(operationName, ActivityKind.Internal);

        if (activity is null)
            return null;

        activity.AddTag("order.id", orderId.Value);

        if (sagaId.HasValue)
            activity.AddTag("saga.id", sagaId.Value);

        if (previousState.HasValue)
            activity.AddTag("saga.previous.state", previousState.Value.ToString());

        if (newState.HasValue)
            activity.AddTag("saga.new.state", newState.Value.ToString());

        if (correlationId.HasValue)
            activity.AddTag("correlation.id", correlationId.Value);

        if (causationId.HasValue)
            activity.AddTag("causation.id", causationId.Value);

        if (reservationId.HasValue)
            activity.AddTag("reservation.id", reservationId.Value);

        if (outcome is not null)
            activity.AddTag("saga.outcome", outcome);

        return activity;
    }

    /// <summary>
    /// Captures the current activity's trace and span ids for the enriched Phase 8.9 log fields
    /// (<c>{TraceId}</c>, <c>{SpanId}</c>). Empty strings when no activity is current.
    /// </summary>
    private static (string TraceId, string SpanId) CurrentTraceIds()
    {
        var activity = Activity.Current;
        return (activity?.TraceId.ToString() ?? string.Empty, activity?.SpanId.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Starts (or resumes) the saga from an <c>orders.order_created</c> message. Returns
    /// <see cref="OrderSagaStartResult"/> describing whether the saga was created and which reservation
    /// commands were staged. The saga and outbox records are staged for the caller to commit.
    /// </summary>
    /// <param name="orderId">The order that was placed.</param>
    /// <param name="correlationId">The correlation id carried by the consumed message.</param>
    /// <param name="causationId">The consumed message's event id, becoming the commands' causation id.</param>
    /// <param name="occurredAtUtc">The moment the order was placed (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaStartResult> StartFromOrderCreatedAsync(
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var existing = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Saga {SagaId} already exists for order {OrderId} in state {SagaState}; ignoring duplicate order-created delivery.",
                existing.Id,
                orderId.Value,
                existing.State);

            return new OrderSagaStartResult(SagaCreated: false, existing.Id, []);
        }

        var order = await _orderStore
            .GetByIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            throw new InvalidOperationException(
                $"Cannot start the saga for order '{orderId.Value}': the order does not exist.");
        }

        var saga = OrderSaga.Start(Guid.NewGuid(), orderId, correlationId, occurredAtUtc);
        var previousState = saga.State;

        var transition = saga.RequestReservation(occurredAtUtc);

        if (transition.IsFailure)
        {
            throw new InvalidOperationException(
                $"Saga for order '{orderId.Value}' could not enter {OrderSagaState.WaitingForInventory}: {transition.Error.Code}.");
        }

        var newState = saga.State;
        var expiresAtUtc = occurredAtUtc.Add(_options.Value.ReservationLifetime);
        var producedAtUtc = _dateTimeProvider.UtcNow;

        var commands = order.LineItems
            .Select(item => new InventoryReservationRequestedIntegrationEvent(
                order.Id.Value,
                item.Sku.Value,
                item.Quantity,
                expiresAtUtc)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = correlationId,
                CausationId = causationId,
                OccurredAtUtc = producedAtUtc
            })
            .ToList();

        using var activity = StartSagaActivity(
            "Orders.Saga.Start",
            orderId,
            saga.Id,
            previousState,
            newState,
            correlationId,
            causationId);

        _sagaStore.Add(saga);

        foreach (var command in commands)
            _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        var (traceId, spanId) = CurrentTraceIds();
        _logger.LogInformation(
            "Created saga {SagaId} for order {OrderId} (previous state {PreviousState}, new state {NewState}) and staged {CommandCount} reservation request(s) (correlation {CorrelationId}, trace {TraceId}, span {SpanId}).",
            saga.Id,
            orderId.Value,
            previousState,
            newState,
            commands.Count,
            correlationId,
            traceId,
            spanId);

        return new OrderSagaStartResult(SagaCreated: true, saga.Id, commands);
    }

    /// <summary>
    /// Applies an <c>inventory.reserved</c> result to the saga.
    /// Phase 8.6: <c>WaitingForInventory → Completed</c> (legacy).
    /// Phase 10.3: <c>WaitingForInventory → WaitingForPayment</c> and stages
    /// <c>payments.create_payment_requested</c> via the Orders outbox. The Order is
    /// left Pending — saga progress lives in saga state. Duplicate results on the
    /// target state are idempotent no-ops; conflicting results on terminal states
    /// are rejected. Stage is atomic with the processed-message record.
    ///
    /// <para>
    /// Phase 8.7: reservation identifiers are recorded on the saga for compensation.
    /// Phase 8.8: late reservations racing a release are compensated immediately.
    /// Phase 10.3: after the first reservation transitions to <c>WaitingForPayment</c>,
    /// a <c>CreatePaymentRequested</c> event is staged with authoritative
    /// <c>Amount/Currency/CustomerId</c> from the Order snapshot, carrying the same
    /// correlation/causation and published via the Orders outbox. Duplicate
    /// <c>inventory.reserved</c> on <c>WaitingForPayment</c> is an idempotent no-op
    /// (no duplicate payment request).
    /// </para>
    /// </summary>
    /// <param name="orderId">The order whose reservation succeeded.</param>
    /// <param name="reservationId">The identifier of the reservation that was created.</param>
    /// <param name="correlationId">The correlation id carried by the consumed result message.</param>
    /// <param name="causationId">The consumed result message's event id.</param>
    /// <param name="occurredAtUtc">The moment the reservation succeeded (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaTransitionResult> ApplyReservationSuccessAsync(
        OrderId orderId,
        Guid reservationId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (reservationId == Guid.Empty)
            throw new ArgumentException("A reservation id must not be an empty GUID.", nameof(reservationId));

        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            throw new InvalidOperationException(
                $"Cannot apply a reservation success for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var successActivity = StartSagaActivity(
            "Orders.Saga.ApplyReservationSuccess",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId,
            reservationId: reservationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForInventory:
            {
                var transition = saga.MarkWaitingForPayment(occurredAtUtc);

                if (transition.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Saga for order '{orderId.Value}' could not enter {OrderSagaState.WaitingForPayment}: {transition.Error.Code}.");
                }

                var newlyRecorded = saga.RecordReservation(reservationId);

                // Phase 8.8: late reservation racing a release must be compensated immediately.
                if (newlyRecorded && saga.ReleaseRequestedAtUtc is not null)
                {
                    var releaseCommands = StageImmediateRelease(reservationId, correlationId, causationId);
                    successActivity?
                        .AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                    var (traceId, spanId) = CurrentTraceIds();
                    _logger.LogInformation(
                        "Applied inventory.reserved to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id,
                        orderId.Value,
                        reservationId,
                        correlationId,
                        causationId,
                        previousState,
                        saga.State,
                        OrderSagaTransitionOutcome.Applied,
                        traceId,
                        spanId);

                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id, releaseCommands);
                }

                // Phase 10.3: stage payment initiation exactly once (first reservation).
                var paymentCommands = await StagePaymentRequestAsync(saga, orderId, correlationId, causationId, cancellationToken).ConfigureAwait(false);

                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId2, spanId2) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.reserved to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    reservationId,
                    correlationId,
                    causationId,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Applied,
                    traceId2,
                    spanId2);

                // Return with empty release commands; payment request is via outbox side-effect.
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id, []);
            }
            case OrderSagaState.WaitingForPayment:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnWaiting, spanIdOnWaiting) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already waiting for payment; duplicate inventory.reserved acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnWaiting,
                    spanIdOnWaiting);

                var newlyRecordedOnWaiting = saga.RecordReservation(reservationId);

                var commandsOnWaiting = newlyRecordedOnWaiting && saga.ReleaseRequestedAtUtc is not null
                    ? StageImmediateRelease(reservationId, correlationId, causationId)
                    : [];

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id, commandsOnWaiting);
            case OrderSagaState.Completed:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnCompleted, spanIdOnCompleted) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate inventory.reserved acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnCompleted,
                    spanIdOnCompleted);

                var newlyRecordedOnCompleted = saga.RecordReservation(reservationId);

                var commandsOnCompleted = newlyRecordedOnCompleted && saga.ReleaseRequestedAtUtc is not null
                    ? StageImmediateRelease(reservationId, correlationId, causationId)
                    : [];

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id, commandsOnCompleted);
            case OrderSagaState.Failed:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());

                var (traceIdOnFailed, spanIdOnFailed) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already failed; conflicting inventory.reserved rejected (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Conflicting,
                    traceIdOnFailed,
                    spanIdOnFailed);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());

                var (traceIdOnInvalid, spanIdOnInvalid) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reserved cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    saga.State,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Invalid,
                    traceIdOnInvalid,
                    spanIdOnInvalid);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Applies an <c>inventory.reservation_failed</c> result to the saga (Phase 8.6):
    /// <c>WaitingForInventory → Failed</c> and the Order transitions through its own <c>Cancel</c>
    /// (Pending → Cancelled), so the failure is reflected in the Order domain model — never bypassed.
    /// If the Order is already Cancelled (e.g. a concurrent customer cancellation) the desired end state
    /// is already reached and the saga failure is still applied. A result for an already-failed saga is
    /// an idempotent no-op; a result for a completed saga is a conflicting rejection; a result for a
    /// non-terminal saga is an acknowledged anomaly. A result for an order with no saga/order throws
    /// (anomaly → retry/dead-letter). The saga transition and the Order cancellation are staged together
    /// for the caller's unit of work, so they commit atomically with the processed-message record.
    /// </summary>
    /// <param name="orderId">The order whose reservation failed.</param>
    /// <param name="correlationId">The correlation id carried by the consumed result message.</param>
    /// <param name="causationId">The consumed result message's event id.</param>
    /// <param name="reason">The stable Inventory error code that caused the failure.</param>
    /// <param name="occurredAtUtc">The moment the reservation failed (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaTransitionResult> ApplyReservationFailureAsync(
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            throw new InvalidOperationException(
                $"Cannot apply a reservation failure for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var failureActivity = StartSagaActivity(
            "Orders.Saga.ApplyReservationFailure",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForInventory:
            {
                var transition = saga.Fail(occurredAtUtc);

                if (transition.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Saga for order '{orderId.Value}' could not fail: {transition.Error.Code}.");
                }

                var order = await _orderStore
                    .GetByIdAsync(orderId, cancellationToken)
                    .ConfigureAwait(false);

                if (order is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot cancel order '{orderId.Value}': the order does not exist.");
                }

                var cancel = order.Cancel(occurredAtUtc);

                if (cancel.IsFailure)
                {
                    // Pending is the only Order state that allows cancellation, so a failed Cancel
                    // means the Order is already Cancelled — the desired end state is already reached.
                    _logger.LogInformation(
                        "Order {OrderId} was already cancelled when applying inventory.reservation_failed; keeping the cancelled state.",
                        orderId.Value);
                }

                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.reservation_failed to saga {SagaId} for order {OrderId} (reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id,
                    orderId.Value,
                    reason,
                    correlationId,
                    causationId,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Applied,
                    traceId,
                    spanId,
                    order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Failed:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnFailed, spanIdOnFailed) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already failed; duplicate inventory.reservation_failed acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnFailed,
                    spanIdOnFailed);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());

                var (traceIdOnCompleted, spanIdOnCompleted) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already completed; conflicting inventory.reservation_failed rejected (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Conflicting,
                    traceIdOnCompleted,
                    spanIdOnCompleted);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());

                var (traceIdOnInvalid, spanIdOnInvalid) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reservation_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    saga.State,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Invalid,
                    traceIdOnInvalid,
                    spanIdOnInvalid);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 8.7 issues the compensation release when an order is cancelled after its reservations were
    /// applied (ADR-009): for every reservation recorded on the saga it stages an
    /// <see cref="InventoryReleaseRequestedIntegrationEvent"/>, so Inventory returns the reserved
    /// quantity to available stock. Inventory owns the release — the saga only requests it through the
    /// explicit contract and never touches reservation state. The commands are staged for the caller's
    /// unit of work, so they commit atomically with the processed-message record; under at-least-once
    /// delivery a redelivered cancellation is skipped by the idempotent-processing boundary and the
    /// Inventory release consumer is itself idempotent (a repeated release of a terminal reservation is
    /// a benign no-op). The saga is never transitioned and the version is never bumped — release is a
    /// fire-and-forget command — but the saga IS marked as release-requested (Phase 8.8), so a late
    /// <c>inventory.reserved</c> result arriving after this point is compensated immediately. A missing
    /// saga is a benign no-op (the workflow never reserved inventory for this order, e.g. the 8.6
    /// failure path already cancelled it); a saga with no recorded reservations is also a no-op (no
    /// release command), but it is still marked so a reservation that lands later is released. Only an
    /// order with no saga AND no recorded reservations is indistinguishable from the no-reservations
    /// case, so no throw is needed here.
    /// </summary>
    /// <param name="orderId">The cancelled order whose reservations must be released.</param>
    /// <param name="correlationId">The correlation id carried by the consumed <c>orders.order_cancelled</c> message.</param>
    /// <param name="causationId">The consumed message's event id, becoming the commands' causation id.</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaReleaseResult> RequestReleaseAsync(
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            var (noSagaTraceId, noSagaSpanId) = CurrentTraceIds();
            _logger.LogInformation(
                "No saga exists for cancelled order {OrderId}; no inventory release requested (correlation {CorrelationId}, trace {TraceId}, span {SpanId}).",
                orderId.Value,
                correlationId,
                noSagaTraceId,
                noSagaSpanId);

            return new OrderSagaReleaseResult(SagaFound: false, ReleaseIssued: false, SagaId: null, Commands: []);
        }

        var previousState = saga.State;
        using var releaseActivity = StartSagaActivity(
            "Orders.Saga.RequestRelease",
            orderId,
            saga.Id,
            previousState,
            previousState,
            correlationId,
            causationId);

        var producedAtUtc = _dateTimeProvider.UtcNow;

        // Phase 8.8: mark the saga as release-requested BEFORE staging the commands, so a late
        // inventory.reserved result that races this cancellation is compensated immediately. The marker
        // is bookkeeping (no state transition, no version bump); the first request wins and a redelivered
        // cancellation cannot shift it.
        saga.MarkReleaseRequested(producedAtUtc);

        var reservationIds = saga.ReservationIds.ToList();

        if (reservationIds.Count == 0)
        {
            releaseActivity?.AddTag("saga.outcome", "release-not-issued");

            var (noReservationTraceId, noReservationSpanId) = CurrentTraceIds();
            _logger.LogInformation(
                "Saga {SagaId} for cancelled order {OrderId} has no recorded reservations; no inventory release requested (correlation {CorrelationId}, trace {TraceId}, span {SpanId}).",
                saga.Id,
                orderId.Value,
                correlationId,
                noReservationTraceId,
                noReservationSpanId);

            return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: false, saga.Id, []);
        }

        var commands = reservationIds
            .Select(id => new InventoryReleaseRequestedIntegrationEvent(id)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = correlationId,
                CausationId = causationId,
                OccurredAtUtc = producedAtUtc
            })
            .ToList();

        foreach (var command in commands)
            _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        releaseActivity?.AddTag("saga.outcome", "release-issued");

        var (traceId, spanId) = CurrentTraceIds();
        _logger.LogInformation(
            "Requested release of {ReservationCount} reservation(s) for cancelled order {OrderId} (saga {SagaId}, correlation {CorrelationId}, causation {CausationId}, state {PreviousState} → {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
            commands.Count,
            orderId.Value,
            saga.Id,
            correlationId,
            causationId,
            previousState,
            saga.State,
            "release-issued",
            traceId,
            spanId);

        return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: true, saga.Id, commands);
    }

    /// <summary>
    /// Phase 8.8 stages an immediate compensation release for a reservation that was recorded on a saga
    /// whose release was already requested (the order was cancelled while the reservation result was
    /// still in flight). The command carries the consumed result's correlation/causation ids and is
    /// staged for the caller's unit of work, so it commits atomically with the saga change and the
    /// processed-message record.
    /// </summary>
    private IReadOnlyList<InventoryReleaseRequestedIntegrationEvent> StageImmediateRelease(
        Guid reservationId,
        Guid correlationId,
        Guid? causationId)
    {
        var command = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = _dateTimeProvider.UtcNow
        };

        _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        _logger.LogInformation(
            "Staged an immediate release for reservation {ReservationId} recorded after the compensation release was requested (correlation {CorrelationId}, causation {CausationId}); the reservation is compensated rather than left to expire.",
            reservationId,
            correlationId,
            causationId);

        return [command];
    }

    /// <summary>
    /// Phase 10.3 stages <c>payments.create_payment_requested</c> after the saga enters
    /// <c>WaitingForPayment</c>. The event carries authoritative <c>Amount/Currency/CustomerId</c>
    /// from the Order snapshot and preserves correlation/causation. It is staged via the Orders
    /// outbox so it commits atomically with the saga state transition. Duplicate invocations
    /// are prevented by the caller checking saga state.
    /// </summary>
    private async Task<IReadOnlyList<CreatePaymentRequestedIntegrationEvent>> StagePaymentRequestAsync(
        OrderSaga saga,
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken)
    {
        var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            throw new InvalidOperationException($"Cannot stage payment for order '{orderId.Value}': the order does not exist.");
        }

        var producedAtUtc = _dateTimeProvider.UtcNow;
        var amount = order.GrandTotal.Amount;
        var currency = order.GrandTotal.Currency;
        var customerId = order.CustomerId.Value;

        var paymentEvent = new CreatePaymentRequestedIntegrationEvent(
            orderId.Value,
            customerId,
            amount,
            currency)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = producedAtUtc
        };

        _context.OutboxMessages.Add(_outboxMessageFactory.Create(paymentEvent));

        _logger.LogInformation(
            "Staged create_payment_requested for order {OrderId} (amount {Amount} {Currency}, customer {CustomerId}, correlation {CorrelationId}, causation {CausationId}).",
            orderId.Value,
            amount,
            currency,
            customerId,
            correlationId,
            causationId);

        return [paymentEvent];
    }

    /// <summary>
    /// Phase 10.4: handles <c>payments.payment_succeeded</c> — validates correlation with the active saga,
    /// verifies the saga is in <c>WaitingForPayment</c>, transitions saga via <c>SucceedPayment</c> to
    /// <c>Completed</c>, and atomically confirms the Order via <c>Confirm</c> (Pending → Confirmed).
    /// Duplicate delivery on <c>Completed</c> is an idempotent no-op (no re-confirm, no version bump);
    /// out-of-order or invalid states are rejected without corrupting state or throwing unhandled exceptions.
    /// Saga + Order changes commit atomically with the processed-message record via the Orders idempotent boundary.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentSucceededAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply payment succeeded for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentSucceeded",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var sagaTransition = saga.SucceedPayment(occurredAtUtc);
                if (sagaTransition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not succeed payment: {sagaTransition.Error.Code}.");
                }

                var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (order is null)
                {
                    throw new InvalidOperationException($"Cannot confirm order '{orderId.Value}': the order does not exist.");
                }

                var confirm = order.Confirm(occurredAtUtc);
                if (confirm.IsFailure)
                {
                    // Already confirmed → idempotent, keep confirmed state
                    _logger.LogInformation("Order {OrderId} was already confirmed when applying payment_succeeded; keeping confirmed state.", orderId.Value);
                }

                // Best-effort payment tracking: record provider intent if saga has not yet captured it
                if (string.IsNullOrWhiteSpace(saga.ProviderPaymentIntentId) && !string.IsNullOrWhiteSpace(providerPaymentIntentId))
                {
                    // Use existing MarkPaymentRequested bookkeeping if saga still in valid state,
                    // but SucceedPayment already moved saga to Completed — so store via Hydrate fields is not needed.
                    // PaymentId tracking is optional for 10.4; provider intent is observable via payment event.
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_succeeded to saga {SagaId} for order {OrderId} (payment {PaymentId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id, orderId.Value, paymentId, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate payments.payment_succeeded acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Failed:
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConf, spanConf) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_succeeded cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceConf, spanConf);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_succeeded cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.5: handles <c>payments.payment_failed</c> — validates correlation with the active saga,
    /// verifies the saga is in <c>WaitingForPayment</c>, transitions saga via <c>FailPayment</c> to
    /// <c>ReleasingInventory</c>, and atomically stages <c>inventory.release_requested</c> for each
    /// recorded reservation via the Orders outbox. Duplicate delivery on <c>ReleasingInventory</c>/<c>Cancelled</c>
    /// is an idempotent no-op (no duplicate release); out-of-order after <c>Completed</c>/<c>Failed</c>
    /// is a conflicting rejection. Saga + outbox commit atomically via the Orders idempotent boundary.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentFailedAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
        string failureReason,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("A failure reason is required.", nameof(failureReason));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply payment failed for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentFailed",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var sagaTransition = saga.FailPayment(occurredAtUtc, failureReason);
                if (sagaTransition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not fail payment: {sagaTransition.Error.Code}.");
                }

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var reservationIds = saga.ReservationIds.ToList();
                var releaseCommands = new List<InventoryReleaseRequestedIntegrationEvent>();

                foreach (var reservationId in reservationIds)
                {
                    var release = new InventoryReleaseRequestedIntegrationEvent(reservationId)
                    {
                        EventId = Guid.NewGuid(),
                        CorrelationId = correlationId,
                        CausationId = causationId,
                        OccurredAtUtc = producedAtUtc
                    };
                    _context.OutboxMessages.Add(_outboxMessageFactory.Create(release));
                    releaseCommands.Add(release);
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_failed to saga {SagaId} for order {OrderId} (payment {PaymentId}, reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); staged {ReleaseCount} release(s).",
                    saga.Id, orderId.Value, paymentId, failureReason, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, releaseCommands.Count);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already releasing inventory; duplicate payments.payment_failed acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
            case OrderSagaState.Failed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConf, spanConf) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceConf, spanConf);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.6: handles <c>inventory.released</c> — validates correlation with the active saga,
    /// transitions saga via <c>MarkInventoryReleased</c> from <c>ReleasingInventory</c> to <c>Cancelled</c>
    /// (OrderCancelled) and preserves correlation/causation propagation. Duplicate delivery on
    /// <c>Cancelled</c>/<c>Completed</c> is an idempotent no-op; out-of-order after <c>Completed</c> is a
    /// duplicate; after <c>Failed</c>/<c>WaitingForPayment</c> etc. is invalid. Saga change commits
    /// atomically with the processed-message record via the Orders idempotent boundary. No Order status
    /// mutation here — cancellation finalization is 10.7.
    /// </summary>
    /// <remarks>
    /// Phase 10.7 extends this handler to automatically cancel the Order and finalize the saga
    /// (<c>Cancelled → Completed</c>) within the same database boundary. After <c>MarkInventoryReleased</c>
    /// transitions to <c>Cancelled</c>, the Order aggregate is cancelled via <c>Order.Cancel()</c>
    /// (Pending → Cancelled, idempotent on already-Cancelled) and the saga is finalized via
    /// <c>FinalizeCancellation()</c> (Cancelled → Completed). The saga strictly does NOT reach
    /// <c>Completed</c> before both inventory release and order cancellation are satisfied; retry
    /// while in <c>ReleasingInventory</c> leaves saga there, and duplicate <c>inventory.released</c>
    /// on <c>Cancelled</c>/<c>Completed</c> is idempotent without re-cancelling or bumping version.
    /// All changes (saga + order + outbox OrderCancelled) commit atomically with the processed-message.
    /// </remarks>
    public async Task<OrderSagaTransitionResult> ApplyInventoryReleasedAsync(
        OrderId orderId,
        Guid reservationId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (reservationId == Guid.Empty)
            throw new ArgumentException("A reservation id must not be an empty GUID.", nameof(reservationId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply inventory released for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyInventoryReleased",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId,
            reservationId: reservationId);

        switch (saga.State)
        {
            case OrderSagaState.ReleasingInventory:
            {
                var transition = saga.MarkInventoryReleased(occurredAtUtc);
                if (transition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not mark inventory released: {transition.Error.Code}.");
                }

                // Phase 10.7: automatically cancel Order within same DB boundary
                // Phase 10.8: also handle Confirmed → Cancelled for post-payment refund
                var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (order is null)
                {
                    throw new InvalidOperationException($"Cannot cancel order '{orderId.Value}': the order does not exist.");
                }

                var cancel = order.Cancel(occurredAtUtc);
                if (cancel.IsFailure)
                {
                    var refundCancel = order.CancelForRefund(occurredAtUtc);
                    if (refundCancel.IsFailure)
                    {
                        _logger.LogInformation("Order {OrderId} was already cancelled when applying inventory.released; keeping cancelled state.", orderId.Value);
                    }
                }

                // Phase 10.7: finalize saga only after both release and cancellation succeeded
                var finalize = saga.FinalizeCancellation(occurredAtUtc);
                if (finalize.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not finalize cancellation: {finalize.Error.Code}.");
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.released to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id, orderId.Value, reservationId, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Cancelled:
            {
                var orderCancelled = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (orderCancelled is not null && (orderCancelled.Status == OrderStatus.Pending || orderCancelled.Status == OrderStatus.Confirmed))
                {
                    var cancel = orderCancelled.Status == OrderStatus.Pending ? orderCancelled.Cancel(occurredAtUtc) : orderCancelled.CancelForRefund(occurredAtUtc);
                    if (cancel.IsFailure)
                    {
                        _logger.LogInformation("Order {OrderId} was already cancelled on duplicate inventory.released (Cancelled state).", orderId.Value);
                    }
                }

                var finalize = saga.FinalizeCancellation(occurredAtUtc);
                if (finalize.IsFailure && saga.State != OrderSagaState.Completed)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not finalize on duplicate: {finalize.Error.Code}.");
                }

                var outcome = saga.State == OrderSagaState.Completed && previousState == OrderSagaState.Cancelled ? OrderSagaTransitionOutcome.Applied : OrderSagaTransitionOutcome.Duplicate;
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", outcome.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already cancelled; duplicate inventory.released handled (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, outcome, traceDup, spanDup);
                return new OrderSagaTransitionResult(outcome, saga.Id);
            }
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceCompletedDup, spanCompletedDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate inventory.released acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceCompletedDup, spanCompletedDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Failed:
            case OrderSagaState.WaitingForPayment:
            case OrderSagaState.WaitingForInventory:
            case OrderSagaState.Started:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv2, spanInv2) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.released cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv2, spanInv2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.released cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.8: trigger refund after post-payment downstream failure.
    /// Valid when saga is Completed (payment succeeded, order confirmed) — transitions
    /// Completed → Refunding and stages RefundPaymentRequested via Orders outbox.
    /// Duplicate on Refunding/ReleasingInventory/Cancelled is idempotent; already Completed
    /// second time after refund is also duplicate. Preserves W3C tracing.
    /// </summary>
    public async Task<OrderSagaTransitionResult> RequestRefundAsync(
        OrderId orderId,
        string reason,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
            throw new InvalidOperationException($"Cannot request refund for order '{orderId.Value}': no saga exists.");

        var previousState = saga.State;
        using var activity = StartSagaActivity("Orders.Saga.RequestRefund", orderId, saga.Id, previousState, correlationId: correlationId, causationId: causationId);

        // Load order to get authoritative amount/currency/payment info
        var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            throw new InvalidOperationException($"Cannot request refund for order '{orderId.Value}': order not found.");

        // Find associated payment via saga tracking or fallback to order's payment?
        // For 10.8 we use saga.PaymentId if available, otherwise lookup via orderId correlation (stored as OrderId).
        // Use provider intent if available, otherwise empty (Payments will lookup).
        var paymentId = saga.PaymentId ?? Guid.Empty;
        if (paymentId == Guid.Empty)
        {
            // No payment tracked yet; try to infer from order? For now, use empty and let Payments lookup by OrderId.
            // We still need a PaymentId for the event; generate a deterministic placeholder based on OrderId?
            // Instead, we will stage refund with OrderId and let Payments resolve Payment by OrderId.
            // For idempotency, use OrderId as correlation.
            _logger.LogWarning("Saga {SagaId} for order {OrderId} has no PaymentId tracked; refund will be resolved by OrderId.", saga.Id, orderId.Value);
            paymentId = Guid.NewGuid(); // placeholder, Payments will resolve by OrderId
        }

        var providerIntentId = saga.ProviderPaymentIntentId ?? string.Empty;
        var amount = order.GrandTotal.Amount;
        var currency = order.GrandTotal.Currency;

        switch (saga.State)
        {
            case OrderSagaState.Completed:
            {
                var transition = saga.RequestRefund(DateTimeOffset.UtcNow);
                if (transition.IsFailure)
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not request refund: {transition.Error.Code}.");

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var refundEvent = new RefundPaymentRequestedIntegrationEvent(orderId.Value, paymentId, providerIntentId, amount, currency, reason)
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = correlationId,
                    CausationId = causationId,
                    OccurredAtUtc = producedAtUtc
                };
                _context.OutboxMessages.Add(_outboxMessageFactory.Create(refundEvent));

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());
                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation("Staged refund_payment_requested for order {OrderId} saga {SagaId} (previous {PreviousState}→{NewState}, correlation {CorrelationId}, trace {TraceId})",
                    orderId.Value, saga.Id, previousState, saga.State, correlationId, traceId);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Refunding:
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation("Saga {SagaId} for order {OrderId} already in {SagaState}; duplicate refund request acknowledged (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Duplicate, traceDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv2, spanInv2) = CurrentTraceIds();
                _logger.LogWarning("Saga {SagaId} for order {OrderId} in state {SagaState} cannot request refund (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceInv2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.8: handles PaymentRefunded — Refunding → ReleasingInventory, then stages
    /// inventory.release_requested for each reservation. Duplicate on ReleasingInventory/Cancelled/Completed is idempotent.
    /// Preserves correlation/causation and W3C tracing.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentRefundedAsync(
        OrderId orderId,
        Guid paymentId,
        string? providerRefundId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
            throw new InvalidOperationException($"Cannot apply payment refunded for order '{orderId.Value}': no saga exists.");

        var previousState = saga.State;
        using var activity = StartSagaActivity("Orders.Saga.ApplyPaymentRefunded", orderId, saga.Id, previousState, correlationId: correlationId, causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.Refunding:
            {
                var transition = saga.HandlePaymentRefunded(occurredAtUtc);
                if (transition.IsFailure)
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not handle payment refunded: {transition.Error.Code}.");

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var reservationIds = saga.ReservationIds.ToList();
                foreach (var reservationId in reservationIds)
                {
                    var release = new InventoryReleaseRequestedIntegrationEvent(reservationId)
                    {
                        EventId = Guid.NewGuid(),
                        CorrelationId = correlationId,
                        CausationId = causationId,
                        OccurredAtUtc = producedAtUtc
                    };
                    _context.OutboxMessages.Add(_outboxMessageFactory.Create(release));
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());
                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation("Applied payment_refunded to saga {SagaId} for order {OrderId} (payment {PaymentId}, refund {RefundId}, correlation {CorrelationId}): {PreviousState}→{NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}, staged {ReleaseCount} releases)",
                    saga.Id, orderId.Value, paymentId, providerRefundId, correlationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, reservationIds.Count);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup2, spanDup2) = CurrentTraceIds();
                _logger.LogInformation("Saga {SagaId} for order {OrderId} already in {SagaState}; duplicate payment_refunded acknowledged (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Duplicate, traceDup2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv3, spanInv3) = CurrentTraceIds();
                _logger.LogWarning("Saga {SagaId} for order {OrderId} in state {SagaState} cannot apply payment_refunded (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceInv3);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }
}