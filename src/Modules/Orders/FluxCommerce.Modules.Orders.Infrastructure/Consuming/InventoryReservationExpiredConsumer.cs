using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 11.4: the Orders consumer for the <c>inventory.reservation_expired</c> result event. It is a
/// thin transport-agnostic adapter (like <see cref="InventoryReservationFailedConsumer"/>): it reads the
/// order id and the Phase 7.8 trace identifiers from the contract and envelope, validates the payload, and
/// delegates to <see cref="OrderSagaOrchestrator.ApplyReservationExpiredAsync"/>. Sub-phase 11.4 deliberately
/// wires the cross-module publication/consumption end-to-end so Inventory's reservation deadline is consumed
/// and acknowledged exactly-once inside the Orders idempotent-processing boundary (Business State +
/// ProcessedMessage commit atomically) — the orchestrator stub performs a safe no-op while the reservation-
/// expiry state model lands in sub-phases 11.5/11.6. Its stable <see cref="HandlerName"/>
/// (<c>Orders.ApplyReservationExpired</c>) scopes the idempotency identity (<c>EventId + HandlerName</c>).
/// The consumer never touches RabbitMQ, messaging transport types, or the DbContext directly.
/// </summary>
public sealed class InventoryReservationExpiredConsumer : IIntegrationEventHandler<InventoryReservationExpiredIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ApplyReservationExpired";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<InventoryReservationExpiredConsumer> _logger;

    public InventoryReservationExpiredConsumer(
        OrderSagaOrchestrator orchestrator,
        ILogger<InventoryReservationExpiredConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        InventoryReservationExpiredIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        Validate(@event);

        _logger.LogInformation(
            "Reservation {ReservationId} for order {OrderId} (sku '{Sku}', quantity {Quantity}) expired at {ExpiredAtUtc}; acknowledging the reservation-expiry event.",
            @event.ReservationId,
            @event.OrderId,
            @event.Sku,
            @event.Quantity,
            @event.ExpiredAtUtc);

        return _orchestrator.ApplyReservationExpiredAsync(
            new OrderId(@event.OrderId),
            @event.ReservationId,
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.ExpiredAtUtc,
            cancellationToken);
    }

    private static void Validate(InventoryReservationExpiredIntegrationEvent @event)
    {
        if (@event.ReservationId == Guid.Empty)
            throw new ArgumentException("A reservation id is required.", nameof(@event));

        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("An order id is required.", nameof(@event));

        if (string.IsNullOrWhiteSpace(@event.Sku))
            throw new ArgumentException("A sku is required.", nameof(@event));

        if (@event.Quantity < 1)
            throw new ArgumentException("A quantity must be positive.", nameof(@event));

        if (@event.ExpiredAtUtc == default)
            throw new ArgumentException("An expiry timestamp is required.", nameof(@event));
    }
}
