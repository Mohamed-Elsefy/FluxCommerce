using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Events;

public sealed class InventoryReservationCommitted : DomainEvent
{
    public InventoryReservationId ReservationId { get; }

    public OrderId OrderId { get; }

    public InventorySku Sku { get; }

    public int Quantity { get; }

    public DateTimeOffset OccurredAt { get; }

    public InventoryReservationCommitted(
        InventoryReservationId reservationId,
        OrderId orderId,
        InventorySku sku,
        int quantity,
        DateTimeOffset occurredAt)
    {
        ReservationId = reservationId;
        OrderId = orderId;
        Sku = sku;
        Quantity = quantity;
        OccurredAt = occurredAt;
    }
}
