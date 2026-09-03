using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ReserveInventoryCommandHandlerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = UtcNow.AddMinutes(15);

    [Fact]
    public async Task HandleAsync_InvalidSku_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), "bad sku!", 1, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidSku", result.Error.Code);
        store.Verify(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyOrderId_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.Empty, "SKU-1", 1, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidOrderId", result.Error.Code);
        store.Verify(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsNotFound_WithoutReserving()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), "SKU-NOPE", 1, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.SkuNotFound", result.Error.Code);
        store.Verify(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InsufficientStock_ReturnsConflict()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store
            .Setup(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryReservation?)null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), "SKU-1", 2, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Inventory.InsufficientStock", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_NonPositiveQuantity_ReturnsValidationError_WithoutReserving()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), "SKU-1", 0, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidQuantity", result.Error.Code);
        store.Verify(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExpirationInThePast_ReturnsValidationError_WithoutReserving()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), "SKU-1", 1, UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidExpiration", result.Error.Code);
        store.Verify(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_ReservesStockAtomicallyAndReturnsReservation()
    {
        var orderId = OrderId.Create();
        var reservation = InventoryReservation.Reserve(
            InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow),
            InventoryReservationId.Create(),
            orderId,
            3,
            ExpiresAt,
            UtcNow).Value;

        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.ExistsAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store
            .Setup(s => s.TryReserveAsync(It.IsAny<InventorySku>(), It.IsAny<InventoryReservationId>(), It.IsAny<OrderId>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(orderId.Value, "SKU-1", 3, ExpiresAt),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        store.Verify(
            s => s.TryReserveAsync(
                new InventorySku("SKU-1"),
                It.IsAny<InventoryReservationId>(),
                orderId,
                3,
                ExpiresAt,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var readModel = result.Value!;
        Assert.Equal(reservation.Id.Value, readModel.ReservationId);
        Assert.Equal(orderId.Value, readModel.OrderId);
        Assert.Equal("SKU-1", readModel.Sku);
        Assert.Equal(3, readModel.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, readModel.Status);
        Assert.Equal(ExpiresAt, readModel.ExpiresAt);
    }

    private static ReserveInventoryCommandHandler CreateHandler(Mock<IInventoryStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault());
}
