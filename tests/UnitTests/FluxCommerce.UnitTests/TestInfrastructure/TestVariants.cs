using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class TestVariants
{
    public static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Variant Create(
        Guid? id = null,
        Guid? productId = null,
        string sku = "SMARTPHONE-128GB",
        string name = "128GB") =>
        Variant.Create(
            id ?? Guid.NewGuid(),
            productId ?? Guid.NewGuid(),
            new Sku(sku),
            name,
            UtcNow);

    public static Variant CreateDeleted(
        Guid? id = null,
        Guid? productId = null,
        string sku = "SMARTPHONE-DELETED",
        string name = "Deleted")
    {
        var variant = Create(id, productId, sku, name);
        variant.SoftDelete(UtcNow);
        return variant;
    }
}
