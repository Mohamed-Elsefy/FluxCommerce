using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProduct;

public sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, Result<ProductResponse>>
{
    private readonly IProductRepository _productRepository;

    public GetProductQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ProductResponse>> HandleAsync(
        GetProductQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.Id, cancellationToken);

        if (product is null)
            return Result.Failure<ProductResponse>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        return Result.Success(ProductResponseMapper.ToResponse(product));
    }
}
