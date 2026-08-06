using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CategoryLifecycleCommandHandlerTests
{
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    [Fact]
    public async Task Activate_ActivatesInactiveCategory()
    {
        var category = TestCategories.Create();
        category.Deactivate();
        await _categories.AddAsync(category);
        var handler = new ActivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateCategoryCommand(category.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CategoryStatus.Active, category.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_CategoryNotFound_ReturnsNotFound()
    {
        var handler = new ActivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_EmptyId_ReturnsValidation()
    {
        var handler = new ActivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateCategoryCommand(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Activate_DeletedCategory_ReturnsConflict()
    {
        var category = TestCategories.CreateDeleted();
        await _categories.AddAsync(category);
        var handler = new ActivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateCategoryCommand(category.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedCategoryCannotBeModified", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Deactivate_DeactivatesActiveCategory()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = new DeactivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateCategoryCommand(category.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CategoryStatus.Inactive, category.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Deactivate_CategoryNotFound_ReturnsNotFound()
    {
        var handler = new DeactivateCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task SoftDelete_MarksCategoryDeleted_WithTimestamp()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = new SoftDeleteCategoryCommandHandler(_categories, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteCategoryCommand(category.Id));

        Assert.True(result.IsSuccess);
        Assert.True(category.IsDeleted);
        Assert.Equal(_clock.UtcNow, category.DeletedAtUtc);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task SoftDelete_CategoryNotFound_ReturnsNotFound()
    {
        var handler = new SoftDeleteCategoryCommandHandler(_categories, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Restore_RestoresDeletedCategory()
    {
        var category = TestCategories.CreateDeleted();
        await _categories.AddAsync(category);
        var handler = new RestoreCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreCategoryCommand(category.Id));

        Assert.True(result.IsSuccess);
        Assert.False(category.IsDeleted);
        Assert.Null(category.DeletedAtUtc);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Restore_CategoryNotFound_ReturnsNotFound()
    {
        var handler = new RestoreCategoryCommandHandler(_categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreCategoryCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }
}
