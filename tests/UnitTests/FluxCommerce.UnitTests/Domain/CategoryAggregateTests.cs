using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CategoryAggregateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    // ---- Creation ----

    [Fact]
    public void Create_CreatesActiveCategory_AndRaisesEvent()
    {
        var category = Category.Create(
            Guid.NewGuid(),
            "Electronics",
            new Slug("electronics"),
            UtcNow,
            "All electronics",
            null);

        Assert.Equal("Electronics", category.Name);
        Assert.Equal("electronics", category.Slug.Value);
        Assert.Equal("All electronics", category.Description);
        Assert.Null(category.ParentId);
        Assert.Equal(CategoryStatus.Active, category.Status);
        Assert.False(category.IsDeleted);
        Assert.Equal(UtcNow, category.CreatedAtUtc);
        Assert.Null(category.DeletedAtUtc);
        Assert.True(category.GetDomainEvents().OfType<CategoryCreated>().Any());
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Category.Create(Guid.Empty, "Electronics", new Slug("electronics"), UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            Category.Create(Guid.NewGuid(), name!, new Slug("electronics"), UtcNow));
    }

    [Fact]
    public void Create_WithNameOverMaxLength_Throws()
    {
        var name = string.Concat(Enumerable.Repeat("a", Category.NameMaxLength + 1));

        Assert.Throws<ArgumentException>(() =>
            Category.Create(Guid.NewGuid(), name, new Slug("electronics"), UtcNow));
    }

    [Fact]
    public void Create_WithItselfAsParent_Throws()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            Category.Create(id, "Electronics", new Slug("electronics"), UtcNow, parentId: id));
    }

    [Fact]
    public void Create_TrimsNameAndDescription()
    {
        var category = Category.Create(
            Guid.NewGuid(),
            "  Electronics  ",
            new Slug("electronics"),
            UtcNow,
            "  All electronics  ");

        Assert.Equal("Electronics", category.Name);
        Assert.Equal("All electronics", category.Description);
    }

    [Fact]
    public void Create_WithEmptyDescription_NormalizesToNull()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow, "   ");

        Assert.Null(category.Description);
    }

    // ---- Rename ----

    [Fact]
    public void Rename_ChangesName_AndRaisesEvent()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.Rename("Appliances");

        Assert.True(result.IsSuccess);
        Assert.Equal("Appliances", category.Name);
        Assert.True(category.GetDomainEvents().OfType<CategoryRenamed>().Any());
    }

    [Fact]
    public void Rename_WithSameName_IsNoOp()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.Rename("  Electronics  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Electronics", category.Name);
        Assert.Empty(category.GetDomainEvents().OfType<CategoryRenamed>());
    }

    [Fact]
    public void Rename_OnDeletedCategory_Fails()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.SoftDelete(UtcNow);

        var result = category.Rename("Appliances");

        Assert.True(result.IsFailure);
        Assert.Equal(CategoryErrors.DeletedCategoryCannotBeModified, result.Error);
    }

    // ---- Description / Slug ----

    [Fact]
    public void ChangeDescription_UpdatesDescription()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.ChangeDescription("Updated");

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated", category.Description);
    }

    [Fact]
    public void ChangeSlug_UpdatesSlug()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.ChangeSlug(new Slug("gadgets"));

        Assert.True(result.IsSuccess);
        Assert.Equal("gadgets", category.Slug.Value);
    }

    // ---- Parent ----

    [Fact]
    public void ChangeParent_UpdatesParent()
    {
        var category = Category.Create(Guid.NewGuid(), "Phones", new Slug("phones"), UtcNow);

        var result = category.ChangeParent(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.NotNull(category.ParentId);
    }

    [Fact]
    public void ChangeParent_ToItself_Fails()
    {
        var category = Category.Create(Guid.NewGuid(), "Phones", new Slug("phones"), UtcNow);

        var result = category.ChangeParent(category.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CategoryErrors.CannotBeOwnParent, result.Error);
    }

    [Fact]
    public void ChangeParent_ToSameParent_IsNoOp()
    {
        var parentId = Guid.NewGuid();
        var category = Category.Create(Guid.NewGuid(), "Phones", new Slug("phones"), UtcNow, parentId: parentId);

        var result = category.ChangeParent(parentId);

        Assert.True(result.IsSuccess);
        Assert.Equal(parentId, category.ParentId);
    }

    // ---- Lifecycle ----

    [Fact]
    public void Activate_ActivatesInactiveCategory_AndRaisesEvent()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.Deactivate();

        var result = category.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CategoryStatus.Active, category.Status);
        Assert.True(category.GetDomainEvents().OfType<CategoryActivated>().Any());
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsNoOp()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.Activate();

        Assert.True(result.IsSuccess);
        Assert.Empty(category.GetDomainEvents().OfType<CategoryActivated>());
    }

    [Fact]
    public void Deactivate_DeactivatesActiveCategory_AndRaisesEvent()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CategoryStatus.Inactive, category.Status);
        Assert.True(category.GetDomainEvents().OfType<CategoryDeactivated>().Any());
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsNoOp()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.Deactivate();
        category.ClearDomainEvents();

        var result = category.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Empty(category.GetDomainEvents().OfType<CategoryDeactivated>());
    }

    [Fact]
    public void SoftDelete_MarksDeleted()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.SoftDelete(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(category.IsDeleted);
        Assert.Equal(UtcNow, category.DeletedAtUtc);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotent()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.SoftDelete(UtcNow);
        var deletedAt = category.DeletedAtUtc;

        var result = category.SoftDelete(UtcNow.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(deletedAt, category.DeletedAtUtc);
    }

    [Fact]
    public void Restore_UnmarksDeleted()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.SoftDelete(UtcNow);

        var result = category.Restore();

        Assert.True(result.IsSuccess);
        Assert.False(category.IsDeleted);
        Assert.Null(category.DeletedAtUtc);
    }

    [Fact]
    public void Restore_WhenNotDeleted_IsNoOp()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);

        var result = category.Restore();

        Assert.True(result.IsSuccess);
        Assert.False(category.IsDeleted);
    }

    [Fact]
    public void DeletedCategory_CannotBeModified()
    {
        var category = Category.Create(Guid.NewGuid(), "Electronics", new Slug("electronics"), UtcNow);
        category.SoftDelete(UtcNow);

        Assert.True(category.Activate().IsFailure);
        Assert.True(category.Deactivate().IsFailure);
        Assert.True(category.ChangeSlug(new Slug("gadgets")).IsFailure);
        Assert.True(category.ChangeDescription("x").IsFailure);
        Assert.True(category.ChangeParent(Guid.NewGuid()).IsFailure);
    }
}
