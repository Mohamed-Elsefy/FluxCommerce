using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CreateCategoryCommandHandlerTests
{
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    private CreateCategoryCommandHandler CreateHandler() =>
        new(_categories, _unitOfWork, _clock, new FakeLocalizer());

    [Fact]
    public async Task Create_Succeeds_AndCreatesCategory()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand(" Electronics ", "electronics", "Consumer electronics", null));

        Assert.True(result.IsSuccess);
        var category = Assert.Single(_categories.Categories);
        Assert.Equal(result.Value, category.Id);
        Assert.Equal("Electronics", category.Name);
        Assert.Equal("electronics", category.Slug.Value);
        Assert.Equal("Consumer electronics", category.Description);
        Assert.Equal(CategoryStatus.Active, category.Status);
        Assert.Null(category.ParentId);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_WithParentId_SetsParent()
    {
        var parent = TestCategories.Create();
        await _categories.AddAsync(parent);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("Phones", "phones", null, parent.Id));

        Assert.True(result.IsSuccess);
        var category = Assert.Single(_categories.Categories, c => c.Id == result.Value);
        Assert.Equal(parent.Id, category.ParentId);
        Assert.Equal(2, _categories.Categories.Count);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsValidation_AndNothingPersisted()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("", "electronics", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_categories.Categories);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_InvalidSlugFormat_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("Electronics", "electronics!", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSlug", result.Error.Code);
        Assert.Empty(_categories.Categories);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await _categories.AddAsync(TestCategories.Create(name: "Electronics", slug: "different"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("Electronics", "electronics", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateCategoryName", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_categories.Categories);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        await _categories.AddAsync(TestCategories.Create(name: "Existing", slug: "electronics"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("New", "electronics", null, null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateCategorySlug", result.Error.Code);
    }

    [Fact]
    public async Task Create_ParentNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("Phones", "phones", null, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ParentCategoryNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_categories.Categories);
    }

    [Fact]
    public async Task Create_ParentDeleted_ReturnsConflict()
    {
        var parent = TestCategories.CreateDeleted();
        await _categories.AddAsync(parent);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateCategoryCommand("Phones", "phones", null, parent.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ParentCategoryDeleted", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }
}
