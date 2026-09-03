using FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CategoryValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- CreateCategoryCommandValidator ----

    [Fact]
    public void CreateCategory_ValidCommand_IsValid()
    {
        var validator = new CreateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new CreateCategoryCommand("Electronics", "electronics", "Description", null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCategory_EmptyName_IsInvalid(string? name)
    {
        var validator = new CreateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new CreateCategoryCommand(name, "electronics", null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateCategory_NameOverMaxLength_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator(Localizer);
        var name = string.Concat(Enumerable.Repeat("a", 101));

        var result = validator.Validate(new CreateCategoryCommand(name, "electronics", null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCategory_EmptySlug_IsInvalid(string? slug)
    {
        var validator = new CreateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new CreateCategoryCommand("Electronics", slug, null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Slug"));
    }

    [Fact]
    public void CreateCategory_SlugOverMaxLength_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator(Localizer);
        var slug = string.Concat(Enumerable.Repeat("a", 256));

        var result = validator.Validate(new CreateCategoryCommand("Electronics", slug, null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Slug"));
    }

    [Fact]
    public void CreateCategory_DescriptionOverMaxLength_IsInvalid()
    {
        var validator = new CreateCategoryCommandValidator(Localizer);
        var description = string.Concat(Enumerable.Repeat("a", 501));

        var result = validator.Validate(new CreateCategoryCommand("Electronics", "electronics", description, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Description"));
    }

    [Fact]
    public void CreateCategory_NullDescription_IsValid()
    {
        var validator = new CreateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new CreateCategoryCommand("Electronics", "electronics", null, null));

        Assert.True(result.IsValid);
    }

    // ---- UpdateCategoryCommandValidator ----

    [Fact]
    public void UpdateCategory_ValidCommand_IsValid()
    {
        var validator = new UpdateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new UpdateCategoryCommand(Guid.NewGuid(), "Electronics", "electronics", null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateCategory_EmptyId_IsInvalid()
    {
        var validator = new UpdateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new UpdateCategoryCommand(Guid.Empty, "Electronics", "electronics", null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    [Fact]
    public void UpdateCategory_EmptyName_IsInvalid()
    {
        var validator = new UpdateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new UpdateCategoryCommand(Guid.NewGuid(), "", "electronics", null, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    // ---- ActivateCategoryCommandValidator ----

    [Fact]
    public void ActivateCategory_ValidId_IsValid()
    {
        var validator = new ActivateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new ActivateCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ActivateCategory_EmptyId_IsInvalid()
    {
        var validator = new ActivateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new ActivateCategoryCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    // ---- DeactivateCategoryCommandValidator ----

    [Fact]
    public void DeactivateCategory_ValidId_IsValid()
    {
        var validator = new DeactivateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeactivateCategory_EmptyId_IsInvalid()
    {
        var validator = new DeactivateCategoryCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateCategoryCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- SoftDeleteCategoryCommandValidator ----

    [Fact]
    public void SoftDeleteCategory_ValidId_IsValid()
    {
        var validator = new SoftDeleteCategoryCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SoftDeleteCategory_EmptyId_IsInvalid()
    {
        var validator = new SoftDeleteCategoryCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteCategoryCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- RestoreCategoryCommandValidator ----

    [Fact]
    public void RestoreCategory_ValidId_IsValid()
    {
        var validator = new RestoreCategoryCommandValidator(Localizer);

        var result = validator.Validate(new RestoreCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RestoreCategory_EmptyId_IsInvalid()
    {
        var validator = new RestoreCategoryCommandValidator(Localizer);

        var result = validator.Validate(new RestoreCategoryCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }
}
