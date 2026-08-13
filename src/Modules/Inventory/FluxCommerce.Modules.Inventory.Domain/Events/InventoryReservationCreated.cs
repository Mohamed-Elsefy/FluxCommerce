using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Events;

public sealed class InventoryReservationCreated : DomainEvent
{
    public InventoryReservationId ReservationId { get; }

    public OrderId OrderId { get; }

    public InventorySku Sku { get; }

    public int Quantity { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset OccurredAt { get; }

    public InventoryReservationCreated(
        InventoryReservationId reservationId,
        OrderId orderId,
        InventorySku sku,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset occurredAt)
    {
        ReservationId = reservationId;
        OrderId = orderId;
        Sku = sku;
        Quantity = quantity;
        ExpiresAt = expiresAt;
        OccurredAt = occurredAt;
    }
}
