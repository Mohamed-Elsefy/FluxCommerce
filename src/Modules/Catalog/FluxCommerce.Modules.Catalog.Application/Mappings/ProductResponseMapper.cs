using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class ProductResponseMapper
{
    public static ProductResponse ToResponse(Product product) =>
        new(
            product.Id,
            product.Name,
            product.Slug.Value,
            product.Description,
            product.Seo is null ? null : ToSeoResponse(product.Seo),
            ToStatus(product.Status),
            ToVisibility(product.Visibility),
            product.CategoryId,
            product.IsDeleted,
            product.CreatedAtUtc,
            product.DeletedAtUtc);

    public static IReadOnlyCollection<ProductResponse> ToResponses(
        IEnumerable<Product> products) =>
        products.Select(ToResponse).ToList();

    private static SeoResponse ToSeoResponse(SeoMetadata seo) =>
        new(seo.Title, seo.Description, seo.Keywords);

    private static ProductStatus ToStatus(Domain.Enums.ProductStatus status) =>
        status switch
        {
            Domain.Enums.ProductStatus.Draft => ProductStatus.Draft,
            Domain.Enums.ProductStatus.Active => ProductStatus.Active,
            Domain.Enums.ProductStatus.Inactive => ProductStatus.Inactive,
            Domain.Enums.ProductStatus.Archived => ProductStatus.Archived,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static ProductVisibility ToVisibility(Domain.Enums.ProductVisibility visibility) =>
        visibility switch
        {
            Domain.Enums.ProductVisibility.Visible => ProductVisibility.Visible,
            Domain.Enums.ProductVisibility.Hidden => ProductVisibility.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };
}
