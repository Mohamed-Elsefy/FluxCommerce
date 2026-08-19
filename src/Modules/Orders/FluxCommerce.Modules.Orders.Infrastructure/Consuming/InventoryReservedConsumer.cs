using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 8.5/8.6: the Orders consumer for the <c>inventory.reserved</c> result event. It is a thin
/// transport-agnostic adapter (like <see cref="OrderCreatedSagaConsumer"/>): it reads the order id and
/// the Phase 7.8 trace identifiers from the contract and envelope, validates the payload, and delegates
/// the whole saga success transition to <see cref="OrderSagaOrchestrator.ApplyReservationSuccessAsync"/>.
/// The saga row update is staged inside the same unit of work as the Orders idempotent-processing
/// boundary (Business State + ProcessedMessage commit atomically), so a redelivered result event is
/// acknowledged without re-execution and a duplicate result on a completed saga is an idempotent no-op.
/// Its stable <see cref="HandlerName"/> (<c>Orders.ApplyReservationSuccess</c>) scopes the idempotency
/// identity (<c>EventId + HandlerName</c>). The consumer never touches RabbitMQ, messaging transport
/// types, or the DbContext directly.
/// </summary>
public sealed class InventoryReservedConsumer : IIntegrationEventHandler<InventoryReservedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyReservationSuccess";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<InventoryReservedConsumer> _logger;

    public InventoryReservedConsumer(
        OrderSagaOrchestrator orchestrator,
        ILogger<InventoryReservedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        InventoryReservedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        Validate(@event);

        _logger.LogInformation(
            "Reservation {ReservationId} for order {OrderId} (sku '{Sku}', quantity {Quantity}) is reserved; applying the saga success transition.",
            @event.ReservationId,
            @event.OrderId,
            @event.Sku,
            @event.Quantity);

        return _orchestrator.ApplyReservationSuccessAsync(
            new OrderId(@event.OrderId),
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            new DateTimeOffset(@event.OccurredAtUtc),
            cancellationToken);
    }

    private static void Validate(InventoryReservedIntegrationEvent @event)
    {
        if (@event.ReservationId == Guid.Empty)
            throw new ArgumentException("A reservation id is required.", nameof(@event));

        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("An order id is required.", nameof(@event));

        if (string.IsNullOrWhiteSpace(@event.Sku))
            throw new ArgumentException("A sku is required.", nameof(@event));

        if (@event.Quantity < 1)
            throw new ArgumentException("A quantity must be positive.", nameof(@event));
    }
}