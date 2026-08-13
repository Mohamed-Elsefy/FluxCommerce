using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Events;

public sealed class InventoryItemCreated : DomainEvent
{
    public InventorySku Sku { get; }

    public int OnHandQuantity { get; }

    public DateTimeOffset OccurredAt { get; }

    public InventoryItemCreated(InventorySku sku, int onHandQuantity, DateTimeOffset occurredAt)
    {
        Sku = sku;
        OnHandQuantity = onHandQuantity;
        OccurredAt = occurredAt;
    }
}
