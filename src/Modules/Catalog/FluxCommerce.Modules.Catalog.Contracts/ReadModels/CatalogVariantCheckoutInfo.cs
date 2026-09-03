using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

/// <summary>
/// Cross-module revalidation result for a single Catalog variant asked by the Orders Application at
/// Checkout. For an <see cref="CatalogVariantEligibilityStatus.Available"/> variant the record also
/// carries the current Catalog data (product identity, SKU, product name and current unit price). The
/// snapshot fields are <see langword="null"/> for non-eligible variants. The unit price is the current
/// authoritative Catalog price and must be used by Checkout pricing instead of the temporary Cart price.
/// </summary>
public sealed record CatalogVariantCheckoutInfo(
    Guid VariantId,
    CatalogVariantEligibilityStatus Status,
    Guid? ProductId = null,
    string? Sku = null,
    string? ProductName = null,
    decimal? UnitPriceAmount = null,
    string? Currency = null);