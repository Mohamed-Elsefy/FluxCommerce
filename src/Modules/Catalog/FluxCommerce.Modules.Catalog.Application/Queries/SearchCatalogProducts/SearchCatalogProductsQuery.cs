using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Queries.SearchCatalogProducts;

public sealed record SearchCatalogProductsQuery(
    string SearchTerm,
    CatalogProductFilter? Filter = null,
    CatalogProductSortBy? SortBy = null,
    CatalogProductSortDirection? SortDirection = null,
    int? PageNumber = null,
    int? PageSize = null)
    : IQuery<Result<PagedResult<CatalogProductListItem>>>;
