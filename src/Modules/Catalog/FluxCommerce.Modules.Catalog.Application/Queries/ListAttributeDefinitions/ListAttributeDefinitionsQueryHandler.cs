using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListAttributeDefinitions;

public sealed class ListAttributeDefinitionsQueryHandler
    : IQueryHandler<ListAttributeDefinitionsQuery, Result<IReadOnlyCollection<AttributeDefinitionResponse>>>
{
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;

    public ListAttributeDefinitionsQueryHandler(IAttributeDefinitionRepository attributeDefinitionRepository)
    {
        _attributeDefinitionRepository = attributeDefinitionRepository;
    }

    public async Task<Result<IReadOnlyCollection<AttributeDefinitionResponse>>> HandleAsync(
        ListAttributeDefinitionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var attributeDefinitions = await _attributeDefinitionRepository.GetAllAsync(cancellationToken);

        return Result.Success(AttributeDefinitionResponseMapper.ToResponses(attributeDefinitions));
    }
}
