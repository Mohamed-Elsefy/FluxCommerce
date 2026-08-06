using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class ProductImageResponseMapper
{
    public static ProductImageResponse ToResponse(
        ProductImage image,
        Guid productId) =>
        new(
            image.Id,
            productId,
            image.ImageUrl,
            image.AltText,
            image.DisplayOrder,
            image.IsPrimary,
            image.Metadata,
            image.CreatedAtUtc);

    public static IReadOnlyCollection<ProductImageResponse> ToResponses(
        Guid productId,
        IEnumerable<ProductImage> images) =>
        images
            .OrderBy(image => image.DisplayOrder)
            .Select(image => ToResponse(image, productId))
            .ToList();
}
