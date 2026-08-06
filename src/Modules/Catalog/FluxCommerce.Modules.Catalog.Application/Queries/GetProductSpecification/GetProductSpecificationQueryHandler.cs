using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductSpecification;

public sealed class GetProductSpecificationQueryHandler
    : IQueryHandler<GetProductSpecificationQuery, Result<ProductSpecificationResponse>>
{
    private readonly IProductRepository _productRepository;

    public GetProductSpecificationQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ProductSpecificationResponse>> HandleAsync(
        GetProductSpecificationQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<ProductSpecificationResponse>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var specification = product.Specifications.FirstOrDefault(
            specification => specification.Id == query.SpecificationId);

        if (specification is null)
            return Result.Failure<ProductSpecificationResponse>(
                Error.NotFound("Catalog.ProductSpecificationNotFound", "The product does not have the specified specification."));

        return Result.Success(ProductSpecificationResponseMapper.ToResponse(specification, product.Id));
    }
}
