using FluxCommerce.Modules.Catalog.Contracts.ReadModels;

namespace FluxCommerce.Modules.Catalog.Contracts.Abstractions;

/// <summary>
/// Cross-module contract that lets the Cart Application resolve whether Catalog variants are
/// eligible to be added to a Cart. Implemented by the Catalog module.
/// </summary>
public interface ICatalogVariantCartLookup
{
    Task<IReadOnlyCollection<CatalogVariantEligibility>> GetCartEligibilityAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);
}
