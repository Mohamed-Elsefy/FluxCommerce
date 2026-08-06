using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogProductDetailsMapper
{
    public static CatalogProductDetails ToDetails(
        Product product,
        IEnumerable<Variant> variants,
        IReadOnlyDictionary<Guid, AttributeDefinition> attributeDefinitions) =>
        new(
            product.Id,
            product.Name,
            product.Slug.Value,
            product.Description,
            product.Seo is null ? null : ProductResponseMapper.ToSeoResponse(product.Seo),
            product.CategoryId,
            CatalogImageSummaryMapper.ToSummaries(product.Images),
            CatalogVariantSummaryMapper.ToSummaries(variants),
            CatalogAttributeSummaryMapper.ToSummaries(product.AttributeValues, attributeDefinitions),
            CatalogSpecificationSummaryMapper.ToSummaries(product.Specifications));
}
