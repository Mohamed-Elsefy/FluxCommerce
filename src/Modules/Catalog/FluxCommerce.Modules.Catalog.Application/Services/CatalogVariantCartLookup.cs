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
/// Resolves whether Catalog variants are eligible to be added to a Cart. A variant is eligible
/// only when it exists, is not deleted, is Active, and belongs to a Product that exists, is not
/// deleted, is Active, and is Visible.
/// </summary>
public sealed class CatalogVariantCartLookup : ICatalogVariantCartLookup
{
    private readonly IVariantRepository _variantRepository;
    private readonly IProductRepository _productRepository;

    public CatalogVariantCartLookup(
        IVariantRepository variantRepository,
        IProductRepository productRepository)
    {
        _variantRepository = variantRepository ?? throw new ArgumentNullException(nameof(variantRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    public async Task<IReadOnlyCollection<CatalogVariantEligibility>> GetCartEligibilityAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        var distinctIds = variantIds.Distinct().ToArray();

        if (distinctIds.Length == 0)
            return Array.Empty<CatalogVariantEligibility>();

        var variants = await LoadVariantsAsync(distinctIds, cancellationToken).ConfigureAwait(false);

        var productIds = variants
            .Where(variant => variant is not null)
            .Select(variant => variant!.ProductId)
            .Distinct()
            .ToArray();

        var products = productIds.Length > 0
            ? await LoadProductsAsync(productIds, cancellationToken).ConfigureAwait(false)
            : Array.Empty<Product>();

        var productsById = products.ToDictionary(product => product.Id);
        var variantsById = variants
            .Where(variant => variant is not null)
            .ToDictionary(variant => variant!.Id, variant => variant!);

        return distinctIds
            .Select(id => ToEligibility(id, variantsById, productsById))
            .ToArray();
    }

    private static CatalogVariantEligibility ToEligibility(
        Guid variantId,
        IReadOnlyDictionary<Guid, Variant> variantsById,
        IReadOnlyDictionary<Guid, Product> productsById)
    {
        if (!variantsById.TryGetValue(variantId, out var variant))
            return new CatalogVariantEligibility(variantId, CatalogVariantEligibilityStatus.Missing);

        if (variant.IsDeleted
            || variant.Status != VariantStatus.Active
            || !productsById.TryGetValue(variant.ProductId, out var product)
            || product.IsDeleted
            || product.Status != ProductStatus.Active
            || product.Visibility != ProductVisibility.Visible)
        {
            return new CatalogVariantEligibility(variantId, CatalogVariantEligibilityStatus.Unavailable);
        }

        return new CatalogVariantEligibility(
            variantId,
            CatalogVariantEligibilityStatus.Available,
            variant.ProductId,
            variant.Sku.Value,
            product.Name);
    }

    private async Task<IReadOnlyCollection<Variant?>> LoadVariantsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var loaded = new List<Variant?>(variantIds.Count);

        foreach (var variantId in variantIds)
        {
            loaded.Add(await _variantRepository.GetByIdAsync(variantId, cancellationToken).ConfigureAwait(false));
        }

        return loaded;
    }

    private async Task<IReadOnlyCollection<Product>> LoadProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var loaded = new List<Product>(productIds.Count);

        foreach (var productId in productIds)
        {
            var product = await _productRepository.GetByIdAsync(productId, cancellationToken).ConfigureAwait(false);

            if (product is not null)
                loaded.Add(product);
        }

        return loaded;
    }
}
