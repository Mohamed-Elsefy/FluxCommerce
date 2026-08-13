using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Events;

public sealed class InventoryStockAdded : DomainEvent
{
    public InventorySku Sku { get; }

    public int Quantity { get; }

    public int OnHandQuantity { get; }

    public DateTimeOffset OccurredAt { get; }

    public InventoryStockAdded(InventorySku sku, int quantity, int onHandQuantity, DateTimeOffset occurredAt)
    {
        Sku = sku;
        Quantity = quantity;
        OnHandQuantity = onHandQuantity;
        OccurredAt = occurredAt;
    }
}
