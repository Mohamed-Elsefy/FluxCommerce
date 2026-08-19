using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 8.5/8.6: the Orders consumer for the <c>inventory.reservation_failed</c> result event. It is a
/// thin transport-agnostic adapter (like <see cref="OrderCreatedSagaConsumer"/>): it reads the order id
/// and the Phase 7.8 trace identifiers from the contract and envelope, validates the payload, and
/// delegates the whole saga failure transition to
/// <see cref="OrderSagaOrchestrator.ApplyReservationFailureAsync"/> — the saga moves to Failed and the
/// Order is cancelled through its own domain transition. Both changes are staged inside the same unit of
/// work as the Orders idempotent-processing boundary (Business State + ProcessedMessage commit
/// atomically), so a redelivered result event is acknowledged without re-execution and a duplicate
/// result on a failed saga is an idempotent no-op. Its stable <see cref="HandlerName"/>
/// (<c>Orders.ApplyReservationFailure</c>) scopes the idempotency identity (<c>EventId + HandlerName</c>).
/// The consumer never touches RabbitMQ, messaging transport types, or the DbContext directly.
/// </summary>
public sealed class InventoryReservationFailedConsumer : IIntegrationEventHandler<InventoryReservationFailedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyReservationFailure";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<InventoryReservationFailedConsumer> _logger;

    public InventoryReservationFailedConsumer(
        OrderSagaOrchestrator orchestrator,
        ILogger<InventoryReservationFailedConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        InventoryReservationFailedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        Validate(@event);

        _logger.LogInformation(
            "Reservation for order {OrderId} (sku '{Sku}', quantity {Quantity}) failed with reason '{Reason}'; applying the saga failure transition.",
            @event.OrderId,
            @event.Sku,
            @event.Quantity,
            @event.Reason);

        return _orchestrator.ApplyReservationFailureAsync(
            new OrderId(@event.OrderId),
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.Reason,
            new DateTimeOffset(@event.OccurredAtUtc),
            cancellationToken);
    }

    private static void Validate(InventoryReservationFailedIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("An order id is required.", nameof(@event));

        if (string.IsNullOrWhiteSpace(@event.Sku))
            throw new ArgumentException("A sku is required.", nameof(@event));

        if (@event.Quantity < 1)
            throw new ArgumentException("A quantity must be positive.", nameof(@event));

        if (string.IsNullOrWhiteSpace(@event.Reason))
            throw new ArgumentException("A failure reason is required.", nameof(@event));
    }
}