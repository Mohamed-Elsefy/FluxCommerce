using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductAttributeValue;

public sealed class GetProductAttributeValueQueryHandler
    : IQueryHandler<GetProductAttributeValueQuery, Result<ProductAttributeValueResponse>>
{
    private readonly IProductRepository _productRepository;
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;

    public GetProductAttributeValueQueryHandler(
        IProductRepository productRepository,
        IAttributeDefinitionRepository attributeDefinitionRepository)
    {
        _productRepository = productRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
    }

    public async Task<Result<ProductAttributeValueResponse>> HandleAsync(
        GetProductAttributeValueQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<ProductAttributeValueResponse>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var attributeValue = product.AttributeValues.FirstOrDefault(
            value => value.AttributeDefinitionId == query.AttributeDefinitionId);

        if (attributeValue is null)
            return Result.Failure<ProductAttributeValueResponse>(
                Error.NotFound("Catalog.ProductAttributeValueNotFound", "The product attribute value was not found."));

        var attributeDefinition = await _attributeDefinitionRepository.GetByIdAsync(
            query.AttributeDefinitionId,
            cancellationToken);

        if (attributeDefinition is null)
            return Result.Failure<ProductAttributeValueResponse>(
                Error.NotFound("Catalog.AttributeDefinitionNotFound", "The attribute definition was not found."));

        return Result.Success(ProductAttributeValueResponseMapper.ToResponse(attributeValue, attributeDefinition, product.Id));
    }
}
