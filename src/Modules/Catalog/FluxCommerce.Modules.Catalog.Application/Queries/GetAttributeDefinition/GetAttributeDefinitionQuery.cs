using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetAttributeDefinition;

public sealed record GetAttributeDefinitionQuery(Guid Id) : IQuery<Result<AttributeDefinitionResponse>>;
