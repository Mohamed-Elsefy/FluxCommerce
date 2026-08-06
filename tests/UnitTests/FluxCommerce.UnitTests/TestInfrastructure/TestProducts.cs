using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class TestProducts
{
    public static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Product Create(
        Guid? id = null,
        string name = "Smartphone",
        string slug = "smartphone",
        Guid? categoryId = null,
        string? description = null,
        SeoMetadata? seo = null,
        ProductVisibility visibility = ProductVisibility.Visible) =>
        Product.Create(
            id ?? Guid.NewGuid(),
            name,
            new Slug(slug),
            categoryId ?? Guid.NewGuid(),
            UtcNow,
            description,
            seo,
            visibility);

    public static Product CreateDeleted(
        Guid? id = null,
        string name = "Deleted Product",
        string slug = "deleted-product")
    {
        var product = Create(id, name, slug);
        product.SoftDelete(UtcNow);
        return product;
    }
}
