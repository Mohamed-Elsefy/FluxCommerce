using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetInventoryItemQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_InvalidSku_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = new GetInventoryItemQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryItemQuery("bad sku!"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidSku", result.Error.Code);
        store.Verify(
            s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsNotFound()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryItem?)null);
        var handler = new GetInventoryItemQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryItemQuery("SKU-NOPE"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.SkuNotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_FoundItem_QueriesByNormalizedSku_AndMapsReadModel()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);
        item.Reserve(3, UtcNow);

        InventorySku? requestedSku = null;
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .Callback<InventorySku, CancellationToken>((sku, _) => requestedSku = sku)
            .ReturnsAsync(item);
        var handler = new GetInventoryItemQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryItemQuery(" sku-1 "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new InventorySku("SKU-1"), requestedSku);

        var readModel = result.Value!;
        Assert.Equal("SKU-1", readModel.Sku);
        Assert.Equal(10, readModel.OnHandQuantity);
        Assert.Equal(3, readModel.ReservedQuantity);
        Assert.Equal(7, readModel.AvailableQuantity);
        Assert.Equal(2, readModel.Version);
    }

    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);
}
