using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductSpecifications;

public sealed class ListProductSpecificationsQueryHandler
    : IQueryHandler<ListProductSpecificationsQuery, Result<IReadOnlyCollection<ProductSpecificationResponse>>>
{
    private readonly IProductRepository _productRepository;

    public ListProductSpecificationsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<IReadOnlyCollection<ProductSpecificationResponse>>> HandleAsync(
        ListProductSpecificationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<IReadOnlyCollection<ProductSpecificationResponse>>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        return Result.Success(ProductSpecificationResponseMapper.ToResponses(
            product.Id,
            product.Specifications));
    }
}
