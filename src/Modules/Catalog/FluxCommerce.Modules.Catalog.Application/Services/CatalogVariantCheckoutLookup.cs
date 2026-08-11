using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using CatalogVariantEligibilityStatus = FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogVariantEligibilityStatus;
using ProductStatus = FluxCommerce.Modules.Catalog.Domain.Enums.ProductStatus;
using ProductVisibility = FluxCommerce.Modules.Catalog.Domain.Enums.ProductVisibility;
using VariantStatus = FluxCommerce.Modules.Catalog.Domain.Enums.VariantStatus;

namespace FluxCommerce.Modules.Catalog.Application.Services;

/// <summary>
/// Resolves the current Catalog state of a batch of variants for the Orders Checkout flow. Uses
/// batch retrieval (<see cref="IVariantRepository.GetByIdsAsync"/> and
/// <see cref="IProductRepository.GetByIdsAsync"/>) so a single requested collection is resolved with
/// a bounded number of queries — never one query per Cart item.
/// </summary>
public sealed class CatalogVariantCheckoutLookup : ICatalogVariantCheckoutLookup
{
    private readonly IVariantRepository _variantRepository;
    private readonly IProductRepository _productRepository;

    public CatalogVariantCheckoutLookup(
        IVariantRepository variantRepository,
        IProductRepository productRepository)
    {
        _variantRepository = variantRepository ?? throw new ArgumentNullException(nameof(variantRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    public async Task<IReadOnlyCollection<CatalogVariantCheckoutInfo>> GetCheckoutInfoAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        var distinctIds = variantIds.Distinct().ToArray();

        if (distinctIds.Length == 0)
            return Array.Empty<CatalogVariantCheckoutInfo>();

        var variants = await _variantRepository.GetByIdsAsync(distinctIds, cancellationToken).ConfigureAwait(false);

        var productIds = variants
            .Select(variant => variant.ProductId)
            .Distinct()
            .ToArray();

        var products = productIds.Length > 0
            ? await _productRepository.GetByIdsAsync(productIds, cancellationToken).ConfigureAwait(false)
            : Array.Empty<Product>();

        var variantsById = variants.ToDictionary(variant => variant.Id);
        var productsById = products.ToDictionary(product => product.Id);

        return distinctIds
            .Select(id => ToCheckoutInfo(id, variantsById, productsById))
            .ToArray();
    }

    private static CatalogVariantCheckoutInfo ToCheckoutInfo(
        Guid variantId,
        IReadOnlyDictionary<Guid, Variant> variantsById,
        IReadOnlyDictionary<Guid, Product> productsById)
    {
        if (!variantsById.TryGetValue(variantId, out var variant))
            return new CatalogVariantCheckoutInfo(variantId, CatalogVariantEligibilityStatus.Missing);

        if (variant.IsDeleted
            || variant.Status != VariantStatus.Active
            || !productsById.TryGetValue(variant.ProductId, out var product)
            || product.IsDeleted
            || product.Status != ProductStatus.Active
            || product.Visibility != ProductVisibility.Visible)
        {
            return new CatalogVariantCheckoutInfo(variantId, CatalogVariantEligibilityStatus.Unavailable);
        }

        return new CatalogVariantCheckoutInfo(
            variantId,
            CatalogVariantEligibilityStatus.Available,
            variant.ProductId,
            variant.Sku.Value,
            product.Name,
            variant.UnitPrice.Amount,
            variant.UnitPrice.Currency);
    }
}