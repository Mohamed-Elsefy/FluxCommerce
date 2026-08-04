using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetCategory;

public sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, Result<CategoryResponse>>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<Result<CategoryResponse>> HandleAsync(
        GetCategoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(query.Id, cancellationToken);

        if (category is null)
            return Result.Failure<CategoryResponse>(
                Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

        return Result.Success(CategoryResponseMapper.ToResponse(category));
    }
}
