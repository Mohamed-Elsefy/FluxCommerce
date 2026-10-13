using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Options;
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
public sealed partial class OrderSagaOrchestrator
{
    private readonly IOrderSagaStore _sagaStore;
    private readonly IOrderAggregateStore _orderStore;
    private readonly OrdersDbContext _context;
    private readonly SagaOutboxMessageFactory _outboxMessageFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOptions<ReservationRequestOptions> _options;
    private readonly ILogger<OrderSagaOrchestrator> _logger;

    public OrderSagaOrchestrator(
        IOrderSagaStore sagaStore,
        IOrderAggregateStore orderStore,
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
}
