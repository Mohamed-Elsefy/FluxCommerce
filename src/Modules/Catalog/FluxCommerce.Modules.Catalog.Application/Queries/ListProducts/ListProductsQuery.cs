using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;

public sealed record ListProductsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? SearchTerm = null,
    ProductStatus? Status = null,
    ProductVisibility? Visibility = null,
    Guid? CategoryId = null)
    : IQuery<Result<PagedResult<ProductResponse>>>;
