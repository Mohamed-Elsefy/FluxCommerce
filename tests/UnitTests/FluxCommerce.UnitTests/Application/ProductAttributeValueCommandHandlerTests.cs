using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.AssignProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.RemoveProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductAttributeValue;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductAttributeValueCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeAttributeDefinitionRepository _attributeDefinitions = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    [Fact]
    public async Task AssignValue_Succeeds_AndAssignsValue()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(product.Id, attributeDefinition.Id, "8GB"));

        Assert.True(result.IsSuccess);
        var value = Assert.Single(product.AttributeValues);
        Assert.Equal(attributeDefinition.Id, value.AttributeDefinitionId);
        Assert.Equal("8GB", value.Value);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task AssignValue_ProductNotFound_ReturnsNotFound()
    {
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), "8GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task AssignValue_AttributeDefinitionNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(product.Id, Guid.NewGuid(), "8GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
        Assert.Empty(product.AttributeValues);
    }

    [Fact]
    public async Task AssignValue_EmptyValue_ReturnsValidation()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(product.Id, Guid.NewGuid(), " "));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task AssignValue_DuplicateValue_ReturnsConflict()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        product.AssignAttributeValue(attributeDefinition, "8GB", _clock.UtcNow);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(product.Id, attributeDefinition.Id, "16GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateProductAttributeValue", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(product.AttributeValues);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task AssignValue_InvalidValueForDataType_ReturnsConflict()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create(dataType: Modules.Catalog.Domain.Enums.AttributeDataType.Number);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new AssignProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new AssignProductAttributeValueCommand(product.Id, attributeDefinition.Id, "not-a-number"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidAttributeValue", result.Error.Code);
    }

    [Fact]
    public async Task UpdateValue_Succeeds_AndUpdatesValue()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        product.AssignAttributeValue(attributeDefinition, "8GB", _clock.UtcNow);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new UpdateProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductAttributeValueCommand(product.Id, attributeDefinition.Id, "16GB"));

        Assert.True(result.IsSuccess);
        Assert.Equal("16GB", product.AttributeValues.Single().Value);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateValue_ProductNotFound_ReturnsNotFound()
    {
        var handler = new UpdateProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), "16GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task UpdateValue_AttributeDefinitionNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new UpdateProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductAttributeValueCommand(product.Id, Guid.NewGuid(), "16GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }

    [Fact]
    public async Task UpdateValue_ValueNotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new UpdateProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductAttributeValueCommand(product.Id, attributeDefinition.Id, "16GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductAttributeValueNotFound", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RemoveValue_Succeeds_AndRemovesValue()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        product.AssignAttributeValue(attributeDefinition, "8GB", _clock.UtcNow);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new RemoveProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RemoveProductAttributeValueCommand(product.Id, attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(product.AttributeValues);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RemoveValue_ProductNotFound_ReturnsNotFound()
    {
        var handler = new RemoveProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RemoveProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task RemoveValue_AttributeDefinitionNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new RemoveProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RemoveProductAttributeValueCommand(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }

    [Fact]
    public async Task RemoveValue_ValueNotFound_ReturnsConflict()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new RemoveProductAttributeValueCommandHandler(_products, _attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RemoveProductAttributeValueCommand(product.Id, attributeDefinition.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductAttributeValueNotFound", result.Error.Code);
    }
}
