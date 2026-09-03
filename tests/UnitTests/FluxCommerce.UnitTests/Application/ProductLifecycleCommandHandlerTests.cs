using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.ArchiveProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductLifecycleCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    [Fact]
    public async Task Activate_ActivatesDraftProduct()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new ActivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_ProductNotFound_ReturnsNotFound()
    {
        var handler = new ActivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_EmptyId_ReturnsValidation()
    {
        var handler = new ActivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateProductCommand(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Activate_InvalidTransition_ReturnsConflict()
    {
        var product = TestProducts.Create();
        product.Archive();
        await _products.AddAsync(product);
        var handler = new ActivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateProductCommand(product.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidProductTransition", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Deactivate_DeactivatesActiveProduct()
    {
        var product = TestProducts.Create();
        product.Activate();
        await _products.AddAsync(product);
        var handler = new DeactivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Inactive, product.Status);
    }

    [Fact]
    public async Task Deactivate_ProductNotFound_ReturnsNotFound()
    {
        var handler = new DeactivateProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Archive_ArchivesActiveProduct()
    {
        var product = TestProducts.Create();
        product.Activate();
        await _products.AddAsync(product);
        var handler = new ArchiveProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ArchiveProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Archived, product.Status);
    }

    [Fact]
    public async Task Archive_ProductNotFound_ReturnsNotFound()
    {
        var handler = new ArchiveProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ArchiveProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task SoftDelete_MarksProductDeleted_WithTimestamp()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new SoftDeleteProductCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.True(product.IsDeleted);
        Assert.Equal(_clock.UtcNow, product.DeletedAtUtc);
    }

    [Fact]
    public async Task SoftDelete_ProductNotFound_ReturnsNotFound()
    {
        var handler = new SoftDeleteProductCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Restore_RestoresDeletedProduct()
    {
        var product = TestProducts.CreateDeleted();
        await _products.AddAsync(product);
        var handler = new RestoreProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.False(product.IsDeleted);
        Assert.Null(product.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_ProductNotFound_ReturnsNotFound()
    {
        var handler = new RestoreProductCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }
}
