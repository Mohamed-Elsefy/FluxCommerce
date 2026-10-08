using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;

public sealed class ListProductsQueryHandler
    : IQueryHandler<ListProductsQuery, Result<PagedResult<ProductResponse>>>
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 20;

    private readonly IProductRepository _productRepository;
    private readonly ILocalizer _localizer;

    public ListProductsQueryHandler(IProductRepository productRepository, ILocalizer localizer)
    {
        _productRepository = productRepository;
        _localizer = localizer;
    }

    public async Task<Result<PagedResult<ProductResponse>>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = new ListProductsQueryValidator(_localizer).Validate(query);

        if (!validation.IsValid)
            return Result.Failure<PagedResult<ProductResponse>>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var pageNumber = query.PageNumber ?? DefaultPageNumber;
        var pageSize = query.PageSize ?? DefaultPageSize;

        var hasFilters = !string.IsNullOrWhiteSpace(query.SearchTerm)
            || query.Status is not null
            || query.Visibility is not null
            || query.CategoryId is not null
            || query.SortBy is not null
            || query.SortDirection is not null;

        if (!hasFilters)
        {
            var result = await _productRepository.GetPagedAsync(pageNumber, pageSize, cancellationToken);

            var items = ProductResponseMapper.ToResponses(result.Items);

            return Result.Success(new PagedResult<ProductResponse>(items, result.TotalCount, pageNumber, pageSize));
        }

        var filtered = await _productRepository.SearchAsync(
            query.SearchTerm ?? string.Empty,
            query.CategoryId,
            query.Status,
            query.Visibility,
            isDeleted: null,
            sortBy: query.SortBy,
            sortDirection: query.SortDirection,
            pageNumber: pageNumber,
            pageSize: pageSize,
            cancellationToken: cancellationToken);

        var filteredItems = ProductResponseMapper.ToResponses(filtered.Items);

        return Result.Success(new PagedResult<ProductResponse>(filteredItems, filtered.TotalCount, pageNumber, pageSize));
    }
}
