using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;

namespace FluxCommerce.Modules.Inventory.Application.Mapping;

/// <summary>
/// Maps Inventory aggregates to the module's application read models.
/// </summary>
public static class InventoryItemReadModelMapper
{
    public static InventoryItemReadModel ToReadModel(InventoryItem item) =>
        new(
            item.Sku.Value,
            item.OnHandQuantity,
            item.ReservedQuantity,
            item.AvailableQuantity,
            item.Version,
            item.UpdatedAtUtc);
}
