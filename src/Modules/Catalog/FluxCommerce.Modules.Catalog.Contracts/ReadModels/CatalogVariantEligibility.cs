using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

/// <summary>
/// Cross-module result for a single Catalog variant asked by the Cart Application. For an
/// <see cref="CatalogVariantEligibilityStatus.Available"/> variant the record also carries the
/// minimal Cart-owned snapshot data (product identity, SKU and product name) that the Cart stores
/// with the CartItem. The snapshot fields are <see langword="null"/> for non-eligible variants.
/// </summary>
public sealed record CatalogVariantEligibility(
    Guid VariantId,
    CatalogVariantEligibilityStatus Status,
    Guid? ProductId = null,
    string? Sku = null,
    string? ProductName = null);
