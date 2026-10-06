using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;

public sealed record ListCategoriesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? SearchTerm = null,
    CategoryStatus? Status = null)
    : IQuery<Result<PagedResult<CategoryResponse>>>;
