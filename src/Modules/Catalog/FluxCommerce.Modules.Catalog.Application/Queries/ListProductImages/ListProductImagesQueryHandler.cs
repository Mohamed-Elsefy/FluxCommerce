using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductImages;

public sealed class ListProductImagesQueryHandler
    : IQueryHandler<ListProductImagesQuery, Result<IReadOnlyCollection<ProductImageResponse>>>
{
    private readonly IProductRepository _productRepository;

    public ListProductImagesQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<IReadOnlyCollection<ProductImageResponse>>> HandleAsync(
        ListProductImagesQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<IReadOnlyCollection<ProductImageResponse>>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        return Result.Success(ProductImageResponseMapper.ToResponses(
            product.Id,
            product.Images));
    }
}
