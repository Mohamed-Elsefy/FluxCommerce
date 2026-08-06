using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductSpecification;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductSpecificationCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    [Fact]
    public async Task CreateSpecification_Succeeds_AndAddsSpecification()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new CreateProductSpecificationCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductSpecificationCommand(product.Id, "Color", "Black"));

        Assert.True(result.IsSuccess);
        var specification = Assert.Single(product.Specifications);
        Assert.Equal("Color", specification.Name);
        Assert.Equal("Black", specification.Value);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task CreateSpecification_ProductNotFound_ReturnsNotFound()
    {
        var handler = new CreateProductSpecificationCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductSpecificationCommand(Guid.NewGuid(), "Color", "Black"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task CreateSpecification_EmptyName_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new CreateProductSpecificationCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductSpecificationCommand(Guid.NewGuid(), " ", "Black"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task CreateSpecification_DuplicateName_ReturnsConflict()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", _clock.UtcNow);
        await _products.AddAsync(product);
        var handler = new CreateProductSpecificationCommandHandler(_products, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductSpecificationCommand(product.Id, "Color", "White"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductSpecification", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(product.Specifications);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateSpecification_Succeeds_AndUpdatesSpecification()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", _clock.UtcNow);
        var specification = product.Specifications.Single();
        await _products.AddAsync(product);
        var handler = new UpdateProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductSpecificationCommand(product.Id, specification.Id, "Color", "White"));

        Assert.True(result.IsSuccess);
        Assert.Equal("White", specification.Value);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateSpecification_ProductNotFound_ReturnsNotFound()
    {
        var handler = new UpdateProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductSpecificationCommand(Guid.NewGuid(), Guid.NewGuid(), "Color", "White"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task UpdateSpecification_NotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new UpdateProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductSpecificationCommand(product.Id, Guid.NewGuid(), "Color", "White"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductSpecificationNotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteSpecification_Succeeds_AndRemovesSpecification()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", _clock.UtcNow);
        var specification = product.Specifications.Single();
        await _products.AddAsync(product);
        var handler = new DeleteProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductSpecificationCommand(product.Id, specification.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(product.Specifications);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task DeleteSpecification_NotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new DeleteProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductSpecificationCommand(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductSpecificationNotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteSpecification_EmptyIds_ReturnsValidation()
    {
        var handler = new DeleteProductSpecificationCommandHandler(_products, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductSpecificationCommand(Guid.Empty, Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }
}
