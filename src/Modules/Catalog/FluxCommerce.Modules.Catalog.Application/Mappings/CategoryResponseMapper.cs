using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CategoryResponseMapper
{
    public static CategoryResponse ToResponse(Category category) =>
        new(
            category.Id,
            category.Name,
            category.Slug.Value,
            category.Description,
            category.ParentId,
            ToStatus(category.Status),
            category.IsDeleted,
            category.CreatedAtUtc,
            category.DeletedAtUtc);

    public static IReadOnlyCollection<CategoryResponse> ToResponses(
        IEnumerable<Category> categories) =>
        categories.Select(ToResponse).ToList();

    private static CategoryStatus ToStatus(Domain.Enums.CategoryStatus status) =>
        status switch
        {
            Domain.Enums.CategoryStatus.Active => CategoryStatus.Active,
            Domain.Enums.CategoryStatus.Inactive => CategoryStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
