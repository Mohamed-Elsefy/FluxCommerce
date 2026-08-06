using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductSpecification;

public sealed record GetProductSpecificationQuery(Guid ProductId, Guid SpecificationId)
    : IQuery<Result<ProductSpecificationResponse>>;
