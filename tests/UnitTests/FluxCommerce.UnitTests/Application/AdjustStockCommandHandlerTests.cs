using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AdjustStockCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_InvalidSku_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new AdjustStockCommand("bad sku!", 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidSku", result.Error.Code);
        store.Verify(
            s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsNotFound_WithoutPersisting()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryItem?)null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new AdjustStockCommand("SKU-NOPE", 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.SkuNotFound", result.Error.Code);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NonPositiveQuantity_ReturnsValidationError_WithoutPersisting()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new AdjustStockCommand("SKU-1", 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidQuantity", result.Error.Code);
        Assert.Equal(10, item.OnHandQuantity);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidAdjustment_AddsStockAndPersists()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);
        int saveChangesCalls = 0;
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        store
            .Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveChangesCalls++)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new AdjustStockCommand("SKU-1", 5),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var readModel = result.Value!;
        Assert.Equal(15, readModel.OnHandQuantity);
        Assert.Equal(0, readModel.ReservedQuantity);
        Assert.Equal(15, readModel.AvailableQuantity);
        Assert.Equal(2, readModel.Version);
        Assert.Equal(1, saveChangesCalls);
        Assert.Equal(15, item.OnHandQuantity);
    }

    [Fact]
    public async Task HandleAsync_NormalizesSku_WhenQuerying()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new AdjustStockCommand(" sku-1 ", 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(
            s => s.GetItemBySkuAsync(new InventorySku("SKU-1"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static AdjustStockCommandHandler CreateHandler(Mock<IInventoryStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault());

    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);
}
