using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;

public sealed class ListCategoriesQueryHandler
    : IQueryHandler<ListCategoriesQuery, Result<PagedResult<CategoryResponse>>>
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 20;

    private readonly ICategoryRepository _categoryRepository;
    private readonly ILocalizer _localizer;

    public ListCategoriesQueryHandler(ICategoryRepository categoryRepository, ILocalizer localizer)
    {
        _categoryRepository = categoryRepository;
        _localizer = localizer;
    }

    public async Task<Result<PagedResult<CategoryResponse>>> HandleAsync(
        ListCategoriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = new ListCategoriesQueryValidator(_localizer).Validate(query);

        if (!validation.IsValid)
            return Result.Failure<PagedResult<CategoryResponse>>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var pageNumber = query.PageNumber ?? DefaultPageNumber;
        var pageSize = query.PageSize ?? DefaultPageSize;

        var hasFilters = !string.IsNullOrWhiteSpace(query.SearchTerm)
            || query.Status is not null;

        if (!hasFilters)
        {
            var result = await _categoryRepository.GetPagedAsync(pageNumber, pageSize, cancellationToken);

            var items = CategoryResponseMapper.ToResponses(result.Items);

            return Result.Success(new PagedResult<CategoryResponse>(items, result.TotalCount, pageNumber, pageSize));
        }

        var filtered = await _categoryRepository.SearchAsync(
            query.SearchTerm ?? string.Empty,
            query.Status,
            pageNumber,
            pageSize,
            cancellationToken);

        var filteredItems = CategoryResponseMapper.ToResponses(filtered.Items);

        return Result.Success(new PagedResult<CategoryResponse>(filteredItems, filtered.TotalCount, pageNumber, pageSize));
    }
}
