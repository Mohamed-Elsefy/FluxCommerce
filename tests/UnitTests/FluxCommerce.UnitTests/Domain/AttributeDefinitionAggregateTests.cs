using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class AttributeDefinitionAggregateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    // ---- Creation ----

    [Fact]
    public void Create_CreatesActiveAttributeDefinition_AndRaisesEvent()
    {
        var attributeDefinition = AttributeDefinition.Create(
            Guid.NewGuid(),
            "RAM",
            new Slug("ram"),
            AttributeDataType.Number,
            UtcNow,
            "Memory",
            true,
            true,
            true,
            5,
            null);

        Assert.Equal("RAM", attributeDefinition.Name);
        Assert.Equal("ram", attributeDefinition.Slug.Value);
        Assert.Equal("Memory", attributeDefinition.Description);
        Assert.Equal(AttributeDataType.Number, attributeDefinition.DataType);
        Assert.True(attributeDefinition.IsRequired);
        Assert.True(attributeDefinition.IsFilterable);
        Assert.True(attributeDefinition.IsSearchable);
        Assert.Equal(5, attributeDefinition.SortOrder);
        Assert.Null(attributeDefinition.CategoryId);
        Assert.Equal(AttributeStatus.Active, attributeDefinition.Status);
        Assert.False(attributeDefinition.IsDeleted);
        Assert.Equal(UtcNow, attributeDefinition.CreatedAtUtc);
        Assert.True(attributeDefinition.GetDomainEvents().OfType<AttributeDefinitionCreated>().Any());
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AttributeDefinition.Create(Guid.Empty, "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            AttributeDefinition.Create(Guid.NewGuid(), name!, new Slug("ram"), AttributeDataType.Text, UtcNow));
    }

    [Fact]
    public void Create_WithNameOverMaxLength_Throws()
    {
        var name = string.Concat(Enumerable.Repeat("a", AttributeDefinition.NameMaxLength + 1));

        Assert.Throws<ArgumentException>(() =>
            AttributeDefinition.Create(Guid.NewGuid(), name, new Slug("ram"), AttributeDataType.Text, UtcNow));
    }

    [Fact]
    public void Create_WithDefaults_AppliesDefaultFlags()
    {
        var attributeDefinition = AttributeDefinition.Create(
            Guid.NewGuid(),
            "RAM",
            new Slug("ram"),
            AttributeDataType.Text,
            UtcNow);

        Assert.False(attributeDefinition.IsRequired);
        Assert.False(attributeDefinition.IsFilterable);
        Assert.False(attributeDefinition.IsSearchable);
        Assert.Equal(0, attributeDefinition.SortOrder);
        Assert.Equal(AttributeStatus.Active, attributeDefinition.Status);
    }

    // ---- Mutations ----

    [Fact]
    public void Rename_ChangesName()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.Rename("Memory");

        Assert.True(result.IsSuccess);
        Assert.Equal("Memory", attributeDefinition.Name);
    }

    [Fact]
    public void ChangeSlug_UpdatesSlug()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeSlug(new Slug("memory"));

        Assert.True(result.IsSuccess);
        Assert.Equal("memory", attributeDefinition.Slug.Value);
    }

    [Fact]
    public void ChangeDataType_UpdatesDataType()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeDataType(AttributeDataType.Number);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttributeDataType.Number, attributeDefinition.DataType);
    }

    [Fact]
    public void ChangeRequired_UpdatesRequired()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeRequired(true);

        Assert.True(result.IsSuccess);
        Assert.True(attributeDefinition.IsRequired);
    }

    [Fact]
    public void ChangeFilterable_UpdatesFilterable()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeFilterable(true);

        Assert.True(result.IsSuccess);
        Assert.True(attributeDefinition.IsFilterable);
    }

    [Fact]
    public void ChangeSearchable_UpdatesSearchable()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeSearchable(true);

        Assert.True(result.IsSuccess);
        Assert.True(attributeDefinition.IsSearchable);
    }

    [Fact]
    public void ChangeSortOrder_UpdatesSortOrder()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.ChangeSortOrder(7);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, attributeDefinition.SortOrder);
    }

    [Fact]
    public void ChangeCategory_UpdatesCategory()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var categoryId = Guid.NewGuid();
        var result = attributeDefinition.ChangeCategory(categoryId);

        Assert.True(result.IsSuccess);
        Assert.Equal(categoryId, attributeDefinition.CategoryId);
    }

    // ---- Lifecycle ----

    [Fact]
    public void Activate_ActivatesInactiveDefinition_AndRaisesEvent()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);
        attributeDefinition.Deactivate();

        var result = attributeDefinition.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(AttributeStatus.Active, attributeDefinition.Status);
        Assert.True(attributeDefinition.GetDomainEvents().OfType<AttributeDefinitionActivated>().Any());
    }

    [Fact]
    public void Deactivate_DeactivatesActiveDefinition_AndRaisesEvent()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var result = attributeDefinition.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(AttributeStatus.Inactive, attributeDefinition.Status);
        Assert.True(attributeDefinition.GetDomainEvents().OfType<AttributeDefinitionDeactivated>().Any());
    }

    [Fact]
    public void SoftDelete_MarksDeleted_AndRestore_Unmarks()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);

        var deleteResult = attributeDefinition.SoftDelete(UtcNow);
        Assert.True(deleteResult.IsSuccess);
        Assert.True(attributeDefinition.IsDeleted);
        Assert.Equal(UtcNow, attributeDefinition.DeletedAtUtc);

        var restoreResult = attributeDefinition.Restore();
        Assert.True(restoreResult.IsSuccess);
        Assert.False(attributeDefinition.IsDeleted);
        Assert.Null(attributeDefinition.DeletedAtUtc);
    }

    [Fact]
    public void DeletedAttributeDefinition_CannotBeModified()
    {
        var attributeDefinition = AttributeDefinition.Create(Guid.NewGuid(), "RAM", new Slug("ram"), AttributeDataType.Text, UtcNow);
        attributeDefinition.SoftDelete(UtcNow);

        Assert.True(attributeDefinition.Rename("Memory").IsFailure);
        Assert.True(attributeDefinition.ChangeSlug(new Slug("memory")).IsFailure);
        Assert.True(attributeDefinition.ChangeDataType(AttributeDataType.Number).IsFailure);
        Assert.True(attributeDefinition.ChangeRequired(true).IsFailure);
        Assert.True(attributeDefinition.ChangeFilterable(true).IsFailure);
        Assert.True(attributeDefinition.ChangeSearchable(true).IsFailure);
        Assert.True(attributeDefinition.ChangeSortOrder(1).IsFailure);
        Assert.True(attributeDefinition.ChangeCategory(Guid.NewGuid()).IsFailure);
        Assert.True(attributeDefinition.Activate().IsFailure);
        Assert.True(attributeDefinition.Deactivate().IsFailure);
        Assert.Equal(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified, attributeDefinition.Rename("Memory").Error);
    }
}
