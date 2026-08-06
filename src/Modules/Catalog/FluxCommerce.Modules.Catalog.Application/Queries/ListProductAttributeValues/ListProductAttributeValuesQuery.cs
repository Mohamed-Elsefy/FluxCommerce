using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductAttributeValues;

public sealed record ListProductAttributeValuesQuery(Guid ProductId)
    : IQuery<Result<IReadOnlyCollection<ProductAttributeValueResponse>>>;
