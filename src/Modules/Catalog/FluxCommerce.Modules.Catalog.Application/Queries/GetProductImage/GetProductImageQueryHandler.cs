using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductImage;

public sealed class GetProductImageQueryHandler
    : IQueryHandler<GetProductImageQuery, Result<ProductImageResponse>>
{
    private readonly IProductRepository _productRepository;

    public GetProductImageQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ProductImageResponse>> HandleAsync(
        GetProductImageQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<ProductImageResponse>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var image = product.Images.FirstOrDefault(image => image.Id == query.ImageId);

        if (image is null)
            return Result.Failure<ProductImageResponse>(
                Error.NotFound("Catalog.ProductImageNotFound", "The product does not have the specified image."));

        return Result.Success(ProductImageResponseMapper.ToResponse(image, product.Id));
    }
}
