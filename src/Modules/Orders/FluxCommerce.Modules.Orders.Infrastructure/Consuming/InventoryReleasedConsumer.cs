using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 10.6: Orders consumer for <c>inventory.released</c> compensation event. Thin adapter that
/// validates payload and delegates to <see cref="OrderSagaOrchestrator.ApplyInventoryReleasedAsync"/>.
/// The saga transition ReleasingInventory → Cancelled is idempotent; duplicate delivery returns success
/// without re-transitioning or corrupting state. Uses OrdersIdempotentProcessor.
/// </summary>
public sealed class InventoryReleasedConsumer : IIntegrationEventHandler<InventoryReleasedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyInventoryReleased";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<InventoryReleasedConsumer> _logger;

    public InventoryReleasedConsumer(OrderSagaOrchestrator orchestrator, ILogger<InventoryReleasedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(InventoryReleasedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);

        _logger.LogInformation("Inventory reservation {ReservationId} for order {OrderId} released; applying saga compensation.",
            @event.ReservationId, @event.OrderId);

        return _orchestrator.ApplyInventoryReleasedAsync(
            new OrderId(@event.OrderId),
            @event.ReservationId,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            new DateTimeOffset(@event.OccurredAtUtc),
            cancellationToken);
    }

    private static void Validate(InventoryReleasedIntegrationEvent @event)
    {
        if (@event.ReservationId == Guid.Empty)
            throw new ArgumentException("ReservationId is required.", nameof(@event));
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.Sku))
            throw new ArgumentException("Sku is required.", nameof(@event));
        if (@event.Quantity < 1)
            throw new ArgumentException("Quantity must be positive.", nameof(@event));
    }
}
