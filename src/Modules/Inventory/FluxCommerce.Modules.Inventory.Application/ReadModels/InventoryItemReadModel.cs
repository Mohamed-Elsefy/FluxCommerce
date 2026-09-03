namespace FluxCommerce.Modules.Inventory.Application.ReadModels;

/// <summary>
/// Read-side projection of an <c>InventoryItem</c>. Available quantity is computed by the domain and
/// is never persisted; the projection surfaces it for operational/inspection use cases.
/// </summary>
public sealed record InventoryItemReadModel(
    string Sku,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    long Version,
    DateTimeOffset UpdatedAtUtc);
