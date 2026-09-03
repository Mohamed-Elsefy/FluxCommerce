using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogImageSummaryMapper
{
    public static CatalogImageSummary ToSummary(ProductImage image) =>
        new(
            image.Id,
            image.ImageUrl,
            image.AltText,
            image.DisplayOrder,
            image.IsPrimary);

    public static IReadOnlyCollection<CatalogImageSummary> ToSummaries(
        IEnumerable<ProductImage> images) =>
        images
            .OrderBy(image => image.DisplayOrder)
            .Select(ToSummary)
            .ToList();
}
