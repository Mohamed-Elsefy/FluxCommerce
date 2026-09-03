using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AttributeDefinitionCommandHandlerTests
{
    private readonly FakeAttributeDefinitionRepository _attributeDefinitions = new();
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    // ---- Create ----

    [Fact]
    public async Task Create_Succeeds_AndCreatesAttributeDefinition()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            "Memory",
            AttributeDataType.Number,
            true,
            true,
            false,
            5,
            null));

        Assert.True(result.IsSuccess);
        var attributeDefinition = Assert.Single(_attributeDefinitions.AttributeDefinitions);
        Assert.Equal(result.Value, attributeDefinition.Id);
        Assert.Equal("RAM", attributeDefinition.Name);
        Assert.Equal("ram", attributeDefinition.Slug.Value);
        Assert.Equal("Memory", attributeDefinition.Description);
        Assert.Equal(AttributeDataType.Number, attributeDefinition.DataType);
        Assert.True(attributeDefinition.IsRequired);
        Assert.True(attributeDefinition.IsFilterable);
        Assert.False(attributeDefinition.IsSearchable);
        Assert.Equal(5, attributeDefinition.SortOrder);
        Assert.Equal(AttributeStatus.Active, attributeDefinition.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_WithCategoryId_SetsCategory()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            category.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(category.Id, _attributeDefinitions.AttributeDefinitions.Single().CategoryId);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_attributeDefinitions.AttributeDefinitions);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_InvalidSlug_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "bad slug!",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSlug", result.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await _attributeDefinitions.AddAsync(TestAttributeDefinitions.Create(name: "RAM", slug: "memory"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateAttributeName", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_attributeDefinitions.AttributeDefinitions);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        await _attributeDefinitions.AddAsync(TestAttributeDefinitions.Create(name: "Other", slug: "ram"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateAttributeSlug", result.Error.Code);
    }

    [Fact]
    public async Task Create_CategoryNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Create_DeletedCategory_ReturnsConflict()
    {
        var category = TestCategories.CreateDeleted();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            category.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedCategoryCannotBeUsed", result.Error.Code);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_Succeeds_AndAppliesChanges()
    {
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            "Memory",
            "memory",
            "Desc",
            AttributeDataType.Text,
            true,
            false,
            true,
            7,
            null));

        Assert.True(result.IsSuccess);
        Assert.Equal("Memory", attributeDefinition.Name);
        Assert.Equal("memory", attributeDefinition.Slug.Value);
        Assert.Equal("Desc", attributeDefinition.Description);
        Assert.True(attributeDefinition.IsRequired);
        Assert.False(attributeDefinition.IsFilterable);
        Assert.True(attributeDefinition.IsSearchable);
        Assert.Equal(7, attributeDefinition.SortOrder);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_AttributeDefinitionNotFound_ReturnsNotFound()
    {
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            Guid.NewGuid(),
            "Memory",
            "memory",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        await _attributeDefinitions.AddAsync(attributeDefinition);
        await _attributeDefinitions.AddAsync(TestAttributeDefinitions.Create(name: "Memory", slug: "memory"));
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            "Memory",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateAttributeName", result.Error.Code);
        Assert.Equal("RAM", attributeDefinition.Name);
    }

    [Fact]
    public async Task Update_DuplicateSlug_ReturnsConflict()
    {
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        await _attributeDefinitions.AddAsync(attributeDefinition);
        await _attributeDefinitions.AddAsync(TestAttributeDefinitions.Create(name: "Memory", slug: "memory"));
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            "RAM",
            "memory",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateAttributeSlug", result.Error.Code);
    }

    [Fact]
    public async Task Update_EmptyName_ReturnsValidation()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            " ",
            attributeDefinition.Slug.Value,
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Update_CategoryNotFound_ReturnsNotFound()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            attributeDefinition.Name,
            attributeDefinition.Slug.Value,
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Update_DeletedCategory_ReturnsConflict()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        var deleted = TestCategories.CreateDeleted();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        await _categories.AddAsync(deleted);
        var handler = new UpdateAttributeDefinitionCommandHandler(_attributeDefinitions, _categories, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateAttributeDefinitionCommand(
            attributeDefinition.Id,
            attributeDefinition.Name,
            attributeDefinition.Slug.Value,
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            deleted.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedCategoryCannotBeUsed", result.Error.Code);
    }

    private CreateAttributeDefinitionCommandHandler CreateHandler() =>
        new(_attributeDefinitions, _categories, _unitOfWork, _clock, new FakeLocalizer());
}
