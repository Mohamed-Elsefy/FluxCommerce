using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class UpdateCategoryCommandHandlerTests
{
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();

    private UpdateCategoryCommandHandler CreateHandler() =>
        new(_categories, _unitOfWork, new FakeLocalizer());

    [Fact]
    public async Task Update_Succeeds_AndAppliesChanges()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            "Computers",
            "computers",
            "New description",
            null));

        Assert.True(result.IsSuccess);
        Assert.Equal("Computers", category.Name);
        Assert.Equal("computers", category.Slug.Value);
        Assert.Equal("New description", category.Description);
        Assert.Null(category.ParentId);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_SetsParent_WhenParentProvided()
    {
        var category = TestCategories.Create();
        var parent = TestCategories.Create(name: "Root", slug: "root");
        await _categories.AddAsync(category);
        await _categories.AddAsync(parent);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            category.Name,
            category.Slug.Value,
            null,
            parent.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, category.ParentId);
    }

    [Fact]
    public async Task Update_CategoryNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(Guid.NewGuid(), "Computers", "computers", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_EmptyName_ReturnsValidation()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(category.Id, " ", category.Slug.Value, null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Update_InvalidSlug_ReturnsValidation()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(category.Id, category.Name, "bad slug!", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSlug", result.Error.Code);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        var category = TestCategories.Create(name: "Electronics", slug: "electronics");
        await _categories.AddAsync(category);
        await _categories.AddAsync(TestCategories.Create(name: "Computers", slug: "computers"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            "Computers",
            "electronics",
            null,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateCategoryName", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Electronics", category.Name);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_SameName_DoesNotTriggerDuplicateCheck()
    {
        var category = TestCategories.Create(name: "Electronics", slug: "electronics");
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            "Electronics",
            "computers",
            null,
            null));

        Assert.True(result.IsSuccess);
        Assert.Equal("computers", category.Slug.Value);
    }

    [Fact]
    public async Task Update_DuplicateSlug_ReturnsConflict()
    {
        var category = TestCategories.Create(name: "Electronics", slug: "electronics");
        await _categories.AddAsync(category);
        await _categories.AddAsync(TestCategories.Create(name: "Computers", slug: "computers"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            "Electronics",
            "computers",
            null,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateCategorySlug", result.Error.Code);
    }

    [Fact]
    public async Task Update_ParentNotFound_ReturnsNotFound()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            category.Name,
            category.Slug.Value,
            null,
            Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ParentCategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Update_ParentDeleted_ReturnsConflict()
    {
        var category = TestCategories.Create();
        var parent = TestCategories.CreateDeleted();
        await _categories.AddAsync(category);
        await _categories.AddAsync(parent);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            category.Name,
            category.Slug.Value,
            null,
            parent.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ParentCategoryDeleted", result.Error.Code);
    }

    [Fact]
    public async Task Update_CategoryItselfAsParent_ReturnsConflict()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateCategoryCommand(
            category.Id,
            category.Name,
            category.Slug.Value,
            null,
            category.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CannotBeOwnParent", result.Error.Code);
    }
}
