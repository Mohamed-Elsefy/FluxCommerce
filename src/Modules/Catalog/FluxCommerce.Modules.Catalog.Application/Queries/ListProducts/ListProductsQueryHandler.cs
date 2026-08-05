using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;

public sealed class ListProductsQueryHandler
    : IQueryHandler<ListProductsQuery, Result<IReadOnlyCollection<ProductResponse>>>
{
    private readonly IProductRepository _productRepository;

    public ListProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<IReadOnlyCollection<ProductResponse>>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllAsync(cancellationToken);

        return Result.Success(ProductResponseMapper.ToResponses(products));
    }
}
