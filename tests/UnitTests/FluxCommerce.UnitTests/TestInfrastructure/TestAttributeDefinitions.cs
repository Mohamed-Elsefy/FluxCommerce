using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class TestAttributeDefinitions
{
    public static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    public static AttributeDefinition Create(
        Guid? id = null,
        string name = "RAM",
        string slug = "ram",
        AttributeDataType dataType = AttributeDataType.Text,
        bool isRequired = false,
        bool isFilterable = false,
        bool isSearchable = false,
        int sortOrder = 0,
        Guid? categoryId = null) =>
        AttributeDefinition.Create(
            id ?? Guid.NewGuid(),
            name,
            new Slug(slug),
            dataType,
            UtcNow,
            null,
            isRequired,
            isFilterable,
            isSearchable,
            sortOrder,
            categoryId);

    public static AttributeDefinition CreateDeleted(
        Guid? id = null,
        string name = "Deleted Attribute",
        string slug = "deleted-attribute")
    {
        var attributeDefinition = Create(id, name, slug);
        attributeDefinition.SoftDelete(UtcNow);
        return attributeDefinition;
    }
}
