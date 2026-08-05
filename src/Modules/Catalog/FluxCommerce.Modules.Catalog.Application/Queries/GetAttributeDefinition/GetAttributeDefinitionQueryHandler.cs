using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetAttributeDefinition;

public sealed class GetAttributeDefinitionQueryHandler
    : IQueryHandler<GetAttributeDefinitionQuery, Result<AttributeDefinitionResponse>>
{
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;

    public GetAttributeDefinitionQueryHandler(IAttributeDefinitionRepository attributeDefinitionRepository)
    {
        _attributeDefinitionRepository = attributeDefinitionRepository;
    }

    public async Task<Result<AttributeDefinitionResponse>> HandleAsync(
        GetAttributeDefinitionQuery query,
        CancellationToken cancellationToken = default)
    {
        var attributeDefinition = await _attributeDefinitionRepository.GetByIdAsync(query.Id, cancellationToken);

        if (attributeDefinition is null)
            return Result.Failure<AttributeDefinitionResponse>(
                Error.NotFound("Catalog.AttributeDefinitionNotFound", "The attribute definition was not found."));

        return Result.Success(AttributeDefinitionResponseMapper.ToResponse(attributeDefinition));
    }
}
