using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.SearchCatalogProducts;

public sealed class SearchCatalogProductsQueryHandler
    : IQueryHandler<SearchCatalogProductsQuery, Result<PagedResult<CatalogProductListItem>>>
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 20;

    private readonly IProductRepository _productRepository;
    private readonly ILocalizer _localizer;

    public SearchCatalogProductsQueryHandler(IProductRepository productRepository, ILocalizer localizer)
    {
        _productRepository = productRepository;
        _localizer = localizer;
    }

    public async Task<Result<PagedResult<CatalogProductListItem>>> HandleAsync(
        SearchCatalogProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = new SearchCatalogProductsQueryValidator(_localizer).Validate(query);

        if (!validation.IsValid)
            return Result.Failure<PagedResult<CatalogProductListItem>>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var pageNumber = query.PageNumber ?? DefaultPageNumber;
        var pageSize = query.PageSize ?? DefaultPageSize;

        if (string.IsNullOrWhiteSpace(query.SearchTerm) && query.Filter is null)
            return Result.Success(
                new PagedResult<CatalogProductListItem>([], 0, pageNumber, pageSize));

        var filter = query.Filter;

        var result = await _productRepository.SearchAsync(
            query.SearchTerm,
            filter?.CategoryId,
            filter?.Status,
            filter?.Visibility,
            filter?.IsDeleted,
            query.SortBy,
            query.SortDirection,
            pageNumber,
            pageSize,
            cancellationToken);

        var items = CatalogProductListItemMapper.ToListItems(result.Items);

        return Result.Success(
            new PagedResult<CatalogProductListItem>(items, result.TotalCount, pageNumber, pageSize));
    }
}
