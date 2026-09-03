using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CreateInventoryItemCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_InvalidSku_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new CreateInventoryItemCommand("bad sku!", 10),
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
    public async Task HandleAsync_NegativeQuantity_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new CreateInventoryItemCommand("SKU-1", -1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidQuantity", result.Error.Code);
        store.Verify(
            s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSku_ReturnsConflict_WithoutPersisting()
    {
        var existing = InventoryItem.Create(new InventorySku("SKU-1"), 5, UtcNow);
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new CreateInventoryItemCommand("SKU-1", 10),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Inventory.DuplicateSku", result.Error.Code);
        store.Verify(
            s => s.GetItemBySkuAsync(new InventorySku("SKU-1"), It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_NormalizesSku_CreatesAndPersists()
    {
        int saveChangesCalls = 0;
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryItem?)null);
        store
            .Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveChangesCalls++)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new CreateInventoryItemCommand(" sku-1 ", 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var item = result.Value!;
        Assert.Equal("SKU-1", item.Sku);
        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
        Assert.Equal(1, saveChangesCalls);

        store.Verify(
            s => s.AddItemAsync(
                It.Is<InventoryItem>(candidate =>
                    candidate.Id == new InventorySku("SKU-1")
                    && candidate.OnHandQuantity == 10
                    && candidate.ReservedQuantity == 0
                    && candidate.Version == InventoryItem.InitialVersion),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            s => s.GetItemBySkuAsync(new InventorySku("SKU-1"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ZeroInitialQuantity_IsAllowed()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryItem?)null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new CreateInventoryItemCommand("SKU-0", 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.OnHandQuantity);
        Assert.Equal(0, result.Value!.AvailableQuantity);
    }

    private static CreateInventoryItemCommandHandler CreateHandler(Mock<IInventoryStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault());

    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);
}
