using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.AddProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.ReorderProductImages;
using FluxCommerce.Modules.Catalog.Application.Commands.SetPrimaryProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductImageCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    // ---- AddProductImage ----

    [Fact]
    public async Task AddImage_Succeeds_AndAddsImage()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new AddProductImageCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AddProductImageCommand(
            product.Id,
            "https://example.com/phone.jpg",
            "A phone",
            true,
            0,
            new Dictionary<string, string> { ["resized"] = "true" }));

        Assert.True(result.IsSuccess);
        var image = Assert.Single(product.Images);
        Assert.Equal("https://example.com/phone.jpg", image.ImageUrl);
        Assert.Equal("A phone", image.AltText);
        Assert.True(image.IsPrimary);
        Assert.Equal(0, image.DisplayOrder);
        Assert.Equal("true", image.Metadata!["resized"]);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task AddImage_ProductNotFound_ReturnsNotFound()
    {
        var handler = new AddProductImageCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AddProductImageCommand(
            Guid.NewGuid(),
            "https://example.com/phone.jpg",
            null,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task AddImage_EmptyUrl_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new AddProductImageCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AddProductImageCommand(
            Guid.NewGuid(),
            "",
            null,
            false,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task AddImage_InvalidDisplayOrder_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new AddProductImageCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AddProductImageCommand(
            Guid.NewGuid(),
            "https://example.com/phone.jpg",
            null,
            false,
            -1,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task AddImage_AddingPrimaryImage_ClearsExistingPrimary()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, true, 0, null, _clock.UtcNow);
        await _products.AddAsync(product);
        var handler = new AddProductImageCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AddProductImageCommand(
            product.Id,
            "https://example.com/b.jpg",
            null,
            true,
            1,
            null));

        Assert.True(result.IsSuccess);
        Assert.Single(product.Images, image => image.IsPrimary);
        Assert.True(product.Images.Single(image => image.ImageUrl == "https://example.com/b.jpg").IsPrimary);
    }

    // ---- UpdateProductImage ----

    [Fact]
    public async Task UpdateImage_Succeeds_AndUpdatesImage()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", "Old", false, 0, null, _clock.UtcNow);
        var image = product.Images.Single();
        await _products.AddAsync(product);
        var handler = new UpdateProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductImageCommand(
            product.Id,
            image.Id,
            "https://example.com/b.jpg",
            "New",
            2,
            null));

        Assert.True(result.IsSuccess);
        Assert.Equal("https://example.com/b.jpg", image.ImageUrl);
        Assert.Equal("New", image.AltText);
        Assert.Equal(2, image.DisplayOrder);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateImage_ProductNotFound_ReturnsNotFound()
    {
        var handler = new UpdateProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductImageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/b.jpg",
            null,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task UpdateImage_ImageNotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new UpdateProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductImageCommand(
            product.Id,
            Guid.NewGuid(),
            "https://example.com/b.jpg",
            null,
            0,
            null));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductImageNotFound", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    // ---- DeleteProductImage ----

    [Fact]
    public async Task DeleteImage_Succeeds_AndRemovesImage()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, _clock.UtcNow);
        var image = product.Images.Single();
        await _products.AddAsync(product);
        var handler = new DeleteProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductImageCommand(product.Id, image.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(product.Images);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task DeleteImage_ImageNotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new DeleteProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductImageCommand(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductImageNotFound", result.Error.Code);
    }

    // ---- SetPrimaryProductImage ----

    [Fact]
    public async Task SetPrimaryImage_Succeeds_AndSetsPrimary()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, _clock.UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, _clock.UtcNow);
        var second = product.Images.Single(image => image.ImageUrl == "https://example.com/b.jpg");
        await _products.AddAsync(product);
        var handler = new SetPrimaryProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new SetPrimaryProductImageCommand(product.Id, second.Id));

        Assert.True(result.IsSuccess);
        Assert.True(second.IsPrimary);
        Assert.Single(product.Images, image => image.IsPrimary);
    }

    [Fact]
    public async Task SetPrimaryImage_ImageNotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new SetPrimaryProductImageCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new SetPrimaryProductImageCommand(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductImageNotFound", result.Error.Code);
    }

    // ---- ReorderProductImages ----

    [Fact]
    public async Task ReorderImages_Succeeds_AndAppliesOrder()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, _clock.UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, _clock.UtcNow);
        product.AddImage("https://example.com/c.jpg", null, false, 2, null, _clock.UtcNow);
        var first = product.Images.Single(image => image.ImageUrl == "https://example.com/a.jpg");
        var second = product.Images.Single(image => image.ImageUrl == "https://example.com/b.jpg");
        var third = product.Images.Single(image => image.ImageUrl == "https://example.com/c.jpg");
        await _products.AddAsync(product);
        var handler = new ReorderProductImagesCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ReorderProductImagesCommand(product.Id, new[] { third.Id, first.Id, second.Id }));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { third.Id, first.Id, second.Id }, product.Images.OrderBy(image => image.DisplayOrder).Select(image => image.Id));
    }

    [Fact]
    public async Task ReorderImages_InvalidOrder_ReturnsConflict()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, _clock.UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, _clock.UtcNow);
        await _products.AddAsync(product);
        var handler = new ReorderProductImagesCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ReorderProductImagesCommand(product.Id, new[] { Guid.NewGuid() }));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidImageOrder", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task ReorderImages_EmptyCollection_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new ReorderProductImagesCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ReorderProductImagesCommand(Guid.NewGuid(), Array.Empty<Guid>()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }
}
