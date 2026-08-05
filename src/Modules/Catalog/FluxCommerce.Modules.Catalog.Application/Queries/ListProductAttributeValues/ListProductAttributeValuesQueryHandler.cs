using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductAttributeValues;

public sealed class ListProductAttributeValuesQueryHandler
    : IQueryHandler<ListProductAttributeValuesQuery, Result<IReadOnlyCollection<ProductAttributeValueResponse>>>
{
    private readonly IProductRepository _productRepository;
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;

    public ListProductAttributeValuesQueryHandler(
        IProductRepository productRepository,
        IAttributeDefinitionRepository attributeDefinitionRepository)
    {
        _productRepository = productRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
    }

    public async Task<Result<IReadOnlyCollection<ProductAttributeValueResponse>>> HandleAsync(
        ListProductAttributeValuesQuery query,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<IReadOnlyCollection<ProductAttributeValueResponse>>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var attributeDefinitions = (await _attributeDefinitionRepository.GetAllAsync(cancellationToken))
            .ToDictionary(attributeDefinition => attributeDefinition.Id);

        return Result.Success(ProductAttributeValueResponseMapper.ToResponses(
            product.Id,
            product.AttributeValues,
            attributeDefinitions));
    }
}
