namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogVariantSummary(
    Guid Id,
    string Sku,
    string Name,
    decimal UnitPriceAmount,
    string Currency);
