using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductAttributeValue;

public sealed record GetProductAttributeValueQuery(Guid ProductId, Guid AttributeDefinitionId)
    : IQuery<Result<ProductAttributeValueResponse>>;
