using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CreateProductCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    private CreateProductCommandHandler CreateHandler() =>
        new(_products, _categories, _unitOfWork, _clock, new FakeLocalizer());

    [Fact]
    public async Task Create_Succeeds_AndCreatesProduct()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand(
            " Smartphone ",
            "smartphone",
            "Latest model",
            category.Id,
            ProductVisibility.Visible));

        Assert.True(result.IsSuccess);
        var product = Assert.Single(_products.Products);
        Assert.Equal(result.Value, product.Id);
        Assert.Equal("Smartphone", product.Name);
        Assert.Equal("smartphone", product.Slug.Value);
        Assert.Equal("Latest model", product.Description);
        Assert.Equal(category.Id, product.CategoryId);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(ProductVisibility.Visible, product.Visibility);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_WithSeoMetadata_SetsSeo()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand(
            "Smartphone",
            "smartphone",
            null,
            category.Id,
            ProductVisibility.Hidden,
            new Modules.Catalog.Application.Commands.SeoMetadataCommand("Phone", "A phone", new[] { "phone", "mobile" })));

        Assert.True(result.IsSuccess);
        var product = Assert.Single(_products.Products);
        Assert.NotNull(product.Seo);
        Assert.Equal("Phone", product.Seo.Title);
        Assert.Equal("A phone", product.Seo.Description);
        Assert.Equal(new[] { "phone", "mobile" }, product.Seo.Keywords);
        Assert.Equal(ProductVisibility.Hidden, product.Visibility);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsValidation()
    {
        await _categories.AddAsync(TestCategories.Create());
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("", "smartphone", null, Guid.NewGuid(), ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_products.Products);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_InvalidSlug_ReturnsValidation()
    {
        await _categories.AddAsync(TestCategories.Create());
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("Smartphone", "bad slug!", null, Guid.NewGuid(), ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSlug", result.Error.Code);
    }

    [Fact]
    public async Task Create_CategoryNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("Smartphone", "smartphone", null, Guid.NewGuid(), ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_products.Products);
    }

    [Fact]
    public async Task Create_DeletedCategory_ReturnsConflict()
    {
        var category = TestCategories.CreateDeleted();
        await _categories.AddAsync(category);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("Smartphone", "smartphone", null, category.Id, ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedCategoryCannotBeUsed", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        await _products.AddAsync(TestProducts.Create(name: "Smartphone", slug: "other"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("Smartphone", "smartphone", null, category.Id, ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductName", result.Error.Code);
        Assert.Single(_products.Products);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        var category = TestCategories.Create();
        await _categories.AddAsync(category);
        await _products.AddAsync(TestProducts.Create(name: "Other", slug: "smartphone"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CreateProductCommand("Smartphone", "smartphone", null, category.Id, ProductVisibility.Visible));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductSlug", result.Error.Code);
    }
}
