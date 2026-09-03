using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class UpdateProductCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();

    private UpdateProductCommandHandler CreateHandler() =>
        new(_products, _categories, _unitOfWork, new FakeLocalizer());

    [Fact]
    public async Task Update_Succeeds_AndAppliesChanges()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            "Phablet",
            "phablet",
            "Big phone",
            category.Id,
            ProductVisibility.Hidden,
            new Modules.Catalog.Application.Commands.SeoMetadataCommand("Title", "Desc", new[] { "k1" })));

        Assert.True(result.IsSuccess);
        Assert.Equal("Phablet", product.Name);
        Assert.Equal("phablet", product.Slug.Value);
        Assert.Equal("Big phone", product.Description);
        Assert.Equal(category.Id, product.CategoryId);
        Assert.Equal(ProductVisibility.Hidden, product.Visibility);
        Assert.NotNull(product.Seo);
        Assert.Equal("Title", product.Seo.Title);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_NullSeo_ClearsSeoMetadata()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var product = TestProducts.Create(seo: SeoMetadata.Create("Title", "Desc", new[] { "k1" }));
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            product.Name,
            product.Slug.Value,
            product.Description,
            category.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsSuccess);
        Assert.Null(product.Seo);
    }

    [Fact]
    public async Task Update_ProductNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            Guid.NewGuid(),
            "Phablet",
            "phablet",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Update_EmptyName_ReturnsValidation()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            "",
            product.Slug.Value,
            null,
            product.CategoryId,
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Update_InvalidSlug_ReturnsValidation()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            product.Name,
            "bad slug!",
            null,
            product.CategoryId,
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSlug", result.Error.Code);
    }

    [Fact]
    public async Task Update_CategoryNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            product.Name,
            product.Slug.Value,
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Update_DeletedCategory_ReturnsConflict()
    {
        var product = TestProducts.Create();
        var deleted = TestCategories.CreateDeleted();
        await _products.AddAsync(product);
        await _categories.AddAsync(deleted);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            product.Name,
            product.Slug.Value,
            null,
            deleted.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedCategoryCannotBeUsed", result.Error.Code);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var product = TestProducts.Create(name: "Smartphone", slug: "smartphone", categoryId: category.Id);
        await _products.AddAsync(product);
        await _products.AddAsync(TestProducts.Create(name: "Tablet", slug: "tablet"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            "Tablet",
            "smartphone",
            null,
            category.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductName", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Smartphone", product.Name);
    }

    [Fact]
    public async Task Update_SameName_DoesNotTriggerDuplicateCheck()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var product = TestProducts.Create(name: "Smartphone", slug: "smartphone", categoryId: category.Id);
        await _products.AddAsync(product);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            "Smartphone",
            "phablet",
            null,
            category.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsSuccess);
        Assert.Equal("phablet", product.Slug.Value);
    }

    [Fact]
    public async Task Update_DuplicateSlug_ReturnsConflict()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var product = TestProducts.Create(name: "Smartphone", slug: "smartphone", categoryId: category.Id);
        await _products.AddAsync(product);
        await _products.AddAsync(TestProducts.Create(name: "Tablet", slug: "tablet"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UpdateProductCommand(
            product.Id,
            "Smartphone",
            "tablet",
            null,
            category.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductSlug", result.Error.Code);
    }
}
