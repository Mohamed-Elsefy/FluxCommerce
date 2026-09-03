using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class ProductSpecificationResponseMapper
{
    public static ProductSpecificationResponse ToResponse(
        ProductSpecification specification,
        Guid productId) =>
        new(
            specification.Id,
            productId,
            specification.Name,
            specification.Value);

    public static IReadOnlyCollection<ProductSpecificationResponse> ToResponses(
        Guid productId,
        IEnumerable<ProductSpecification> specifications) =>
        specifications
            .Select(specification => ToResponse(specification, productId))
            .ToList();
}
