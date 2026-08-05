using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListAttributeDefinitions;

public sealed record ListAttributeDefinitionsQuery() : IQuery<Result<IReadOnlyCollection<AttributeDefinitionResponse>>>;
