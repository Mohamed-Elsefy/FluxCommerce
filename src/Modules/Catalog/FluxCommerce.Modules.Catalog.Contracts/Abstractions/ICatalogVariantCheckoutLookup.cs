using FluxCommerce.Modules.Catalog.Contracts.ReadModels;

namespace FluxCommerce.Modules.Catalog.Contracts.Abstractions;

/// <summary>
/// Cross-module contract that lets the Orders Application batch-revalidate Catalog variants at
/// Checkout. Implemented by the Catalog module using batch retrieval — Orders must never issue one
/// request per Cart item (no N+1). The variants are resolved against the current Catalog state.
/// </summary>
public interface ICatalogVariantCheckoutLookup
{
    Task<IReadOnlyCollection<CatalogVariantCheckoutInfo>> GetCheckoutInfoAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);
}