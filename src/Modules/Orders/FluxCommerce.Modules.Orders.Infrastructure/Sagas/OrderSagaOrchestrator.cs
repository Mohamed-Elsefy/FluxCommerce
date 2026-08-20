using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
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
/// transition, no version bump. Known limitation (deferred to Phase 8.8 hardening): a very late
/// <c>inventory.reserved</c> result racing the cancellation could record a reservation AFTER the release
/// was requested, leaving that reservation unreleased until Inventory expiry (Phase 11).
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

        var transition = saga.RequestReservation(occurredAtUtc);

        if (transition.IsFailure)
        {
            throw new InvalidOperationException(
                $"Saga for order '{orderId.Value}' could not enter {OrderSagaState.WaitingForInventory}: {transition.Error.Code}.");
        }

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

        _sagaStore.Add(saga);

        foreach (var command in commands)
            _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        _logger.LogInformation(
            "Created saga {SagaId} for order {OrderId} ({SagaState}) and staged {CommandCount} reservation request(s) (correlation {CorrelationId}).",
            saga.Id,
            orderId.Value,
            saga.State,
            commands.Count,
            correlationId);

        return new OrderSagaStartResult(SagaCreated: true, saga.Id, commands);
    }

    /// <summary>
    /// Applies an <c>inventory.reserved</c> result to the saga (Phase 8.6): <c>WaitingForInventory →
    /// Completed</c>. The Order is left Pending — the saga progress lives in saga state, not in
    /// <c>Order.Status</c> (the Order domain model has no further forward status until payment is
    /// initiated). A result for an already-completed saga is an idempotent no-op; a result for a failed
    /// saga is a conflicting rejection; a result for a non-terminal saga is an acknowledged anomaly. A
    /// result for an order with no saga throws (anomaly → retry/dead-letter). The transition is staged
    /// for the caller's unit of work.
    ///
    /// <para>
    /// Phase 8.7: the reservation identifier is recorded on the saga whenever the result is applied
    /// (whether the transition <see cref="OrderSagaTransitionOutcome.Applied"/> or a duplicate delivery
    /// <see cref="OrderSagaTransitionOutcome.Duplicate"/> on an already-completed saga — a multi-line
    /// order reports its line reservations one result at a time, so later results arrive on the
    /// completed saga). The recorded identifiers drive the compensation release issued when the order is
    /// cancelled. A conflicting (<see cref="OrderSagaTransitionOutcome.Conflicting"/>) result is never
    /// recorded: a reservation created after the saga failed is not the saga's to release.
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

        switch (saga.State)
        {
            case OrderSagaState.WaitingForInventory:
            {
                var transition = saga.Complete(occurredAtUtc);

                if (transition.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Saga for order '{orderId.Value}' could not complete: {transition.Error.Code}.");
                }

                saga.RecordReservation(reservationId);

                _logger.LogInformation(
                    "Applied inventory.reserved to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): now {SagaState}.",
                    saga.Id,
                    orderId.Value,
                    reservationId,
                    correlationId,
                    causationId,
                    saga.State);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Completed:
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate inventory.reserved acknowledged.",
                    saga.Id,
                    orderId.Value);

                // Phase 8.7: a later line-item reservation arrives on the completed saga; record it so the
                // compensation release covers every reservation the Inventory module actually created.
                saga.RecordReservation(reservationId);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Failed:
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already failed; conflicting inventory.reserved rejected.",
                    saga.Id,
                    orderId.Value);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reserved cannot be applied.",
                    saga.Id,
                    orderId.Value,
                    saga.State);

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

                _logger.LogInformation(
                    "Applied inventory.reservation_failed to saga {SagaId} for order {OrderId} (reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): now {SagaState}; order is {OrderStatus}.",
                    saga.Id,
                    orderId.Value,
                    reason,
                    correlationId,
                    causationId,
                    saga.State,
                    order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Failed:
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already failed; duplicate inventory.reservation_failed acknowledged.",
                    saga.Id,
                    orderId.Value);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already completed; conflicting inventory.reservation_failed rejected.",
                    saga.Id,
                    orderId.Value);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reservation_failed cannot be applied.",
                    saga.Id,
                    orderId.Value,
                    saga.State);

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
    /// a benign no-op). The saga is never touched: no state transition, no version bump — release is a
    /// fire-and-forget command. A missing saga is a benign no-op (the workflow never reserved inventory
    /// for this order, e.g. the 8.6 failure path already cancelled it); a saga with no recorded
    /// reservations is also a no-op. Only an order with no saga AND no recorded reservations is
    /// indistinguishable from the no-reservations case, so no throw is needed here.
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
            _logger.LogInformation(
                "No saga exists for cancelled order {OrderId}; no inventory release requested (correlation {CorrelationId}).",
                orderId.Value,
                correlationId);

            return new OrderSagaReleaseResult(SagaFound: false, ReleaseIssued: false, SagaId: null, Commands: []);
        }

        var reservationIds = saga.ReservationIds.ToList();

        if (reservationIds.Count == 0)
        {
            _logger.LogInformation(
                "Saga {SagaId} for cancelled order {OrderId} has no recorded reservations; no inventory release requested (correlation {CorrelationId}).",
                saga.Id,
                orderId.Value,
                correlationId);

            return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: false, saga.Id, []);
        }

        var producedAtUtc = _dateTimeProvider.UtcNow;

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

        _logger.LogInformation(
            "Requested release of {ReservationCount} reservation(s) for cancelled order {OrderId} (saga {SagaId}, correlation {CorrelationId}, causation {CausationId}).",
            commands.Count,
            orderId.Value,
            saga.Id,
            correlationId,
            causationId);

        return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: true, saga.Id, commands);
    }
}