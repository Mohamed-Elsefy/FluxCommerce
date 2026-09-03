using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogProductDetails(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    SeoResponse? Seo,
    Guid CategoryId,
    IReadOnlyCollection<CatalogImageSummary> Images,
    IReadOnlyCollection<CatalogVariantSummary> Variants,
    IReadOnlyCollection<CatalogAttributeSummary> Attributes,
    IReadOnlyCollection<CatalogSpecificationSummary> Specifications);
