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
}