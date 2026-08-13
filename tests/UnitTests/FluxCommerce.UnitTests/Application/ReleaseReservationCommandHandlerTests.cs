using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ReleaseReservationCommandHandlerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = UtcNow.AddMinutes(15);

    [Fact]
    public async Task HandleAsync_EmptyReservationId_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReleaseReservationCommand(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidReservationId", result.Error.Code);
        store.Verify(
            s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingReservation_ReturnsNotFound_WithoutPersisting()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryReservation?)null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReleaseReservationCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.ReservationNotFound", result.Error.Code);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsNotFound_WithoutPersisting()
    {
        var (item, reservation) = CreateReservedState(10, 3);
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryItem?)null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReleaseReservationCommand(reservation.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.SkuNotFound", result.Error.Code);
        Assert.Equal(3, item.ReservedQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PendingReservation_ReleasesQuantityAndPersists()
    {
        var (item, reservation) = CreateReservedState(10, 3);
        int saveChangesCalls = 0;
        var store = CreateStore(item, reservation);
        store
            .Setup(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveChangesCalls++)
            .ReturnsAsync(true);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new ReleaseReservationCommand(reservation.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
        Assert.Equal(1, saveChangesCalls);

        var readModel = result.Value!;
        Assert.Equal(InventoryReservationStatus.Released, readModel.Status);
        Assert.Equal(reservation.Id.Value, readModel.ReservationId);
    }

    [Fact]
    public async Task HandleAsync_RepeatedRelease_ReturnsConflict_AndDoesNotDoubleRelease()
    {
        var (item, reservation) = CreateReservedState(10, 3);
        var store = CreateStore(item, reservation);
        var handler = CreateHandler(store);

        var first = await handler.HandleAsync(
            new ReleaseReservationCommand(reservation.Id.Value),
            CancellationToken.None);
        var second = await handler.HandleAsync(
            new ReleaseReservationCommand(reservation.Id.Value),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal("Inventory.InvalidStateTransition", second.Error.Code);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (InventoryItem Item, InventoryReservation Reservation) CreateReservedState(
        int onHand,
        int quantity)
    {
        var item = InventoryItem.Create(new InventorySku("SKU-1"), onHand, UtcNow);
        var reservation = InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            quantity,
            ExpiresAt,
            UtcNow).Value;

        return (item, reservation);
    }

    private static Mock<IInventoryStore> CreateStore(InventoryItem item, InventoryReservation reservation)
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        store
            .Setup(s => s.GetItemBySkuAsync(It.IsAny<InventorySku>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        store
            .Setup(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return store;
    }

    private static ReleaseReservationCommandHandler CreateHandler(Mock<IInventoryStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault());
}
