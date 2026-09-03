using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class TestCategories
{
    public static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Category Create(
        Guid? id = null,
        string name = "Electronics",
        string slug = "electronics",
        string? description = null,
        Guid? parentId = null) =>
        Category.Create(
            id ?? Guid.NewGuid(),
            name,
            new Slug(slug),
            UtcNow,
            description,
            parentId);

    public static Category CreateDeleted(
        Guid? id = null,
        string name = "Deleted Category",
        string slug = "deleted-category")
    {
        var category = Create(id, name, slug);
        category.SoftDelete(UtcNow);
        return category;
    }
}
