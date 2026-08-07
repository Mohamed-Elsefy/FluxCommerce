using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;

public sealed record ListProductsQuery(
    int? PageNumber = null,
    int? PageSize = null)
    : IQuery<Result<PagedResult<ProductResponse>>>;
