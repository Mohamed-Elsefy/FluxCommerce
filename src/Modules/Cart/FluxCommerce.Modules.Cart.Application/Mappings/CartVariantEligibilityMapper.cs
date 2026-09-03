using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Mappings;

/// <summary>
/// Maps Catalog variant eligibility results to the shared Cart Application Error model and, for
/// eligible variants, to the Cart-owned snapshot data used to build a CartItem.
/// </summary>
public static class CartVariantEligibilityMapper
{
    public static Error? ToError(CatalogVariantEligibility eligibility)
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        return eligibility.Status switch
        {
            CatalogVariantEligibilityStatus.Missing => CartErrorMapper.ToApplicationError(CartErrors.VariantNotFound),
            CatalogVariantEligibilityStatus.Unavailable => CartErrorMapper.ToApplicationError(CartErrors.VariantUnavailable),
            _ => null,
        };
    }

    /// <summary>
    /// Converts an <see cref="CatalogVariantEligibilityStatus.Available"/> eligibility result into
    /// the snapshot data that the Cart persists with the CartItem. The product identity, SKU and
    /// product name are authoritative from the Catalog contract. Throws when the result is not an
    /// eligible variant or is missing snapshot fields so callers can map it to a safe failure.
    /// </summary>
    public static CartItemSnapshotData ToSnapshotData(CatalogVariantEligibility eligibility)
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        if (eligibility.Status != CatalogVariantEligibilityStatus.Available)
            throw new InvalidOperationException("Snapshot data is only available for an eligible variant.");

        if (eligibility.ProductId is null)
            throw new InvalidOperationException("A Catalog variant eligibility result is missing its product id.");

        if (string.IsNullOrWhiteSpace(eligibility.Sku))
            throw new InvalidOperationException("A Catalog variant eligibility result is missing its SKU.");

        if (string.IsNullOrWhiteSpace(eligibility.ProductName))
            throw new InvalidOperationException("A Catalog variant eligibility result is missing its product name.");

        var productName = eligibility.ProductName.Trim();

        if (productName.Length > CartItem.ProductNameMaxLength)
            throw new InvalidOperationException("A Catalog variant eligibility result has an invalid product name length.");

        return new CartItemSnapshotData(
            new ProductId(eligibility.ProductId.Value),
            new VariantId(eligibility.VariantId),
            new Sku(eligibility.Sku),
            productName);
    }
}

/// <summary>
/// The Cart-owned snapshot carried by a CartItem. Populated from the Catalog contract and persisted
/// by the Cart store; never a reference to a Catalog entity.
/// </summary>
public sealed record CartItemSnapshotData(
    ProductId ProductId,
    VariantId VariantId,
    Sku Sku,
    string ProductName);
