using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductImage;

public sealed record GetProductImageQuery(Guid ProductId, Guid ImageId)
    : IQuery<Result<ProductImageResponse>>;
