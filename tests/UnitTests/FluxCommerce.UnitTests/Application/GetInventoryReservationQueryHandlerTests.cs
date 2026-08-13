using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryReservation;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetInventoryReservationQueryHandlerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_EmptyReservationId_ReturnsValidationError_WithoutQuerying()
    {
        var store = new Mock<IInventoryStore>();
        var handler = new GetInventoryReservationQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryReservationQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Inventory.InvalidReservationId", result.Error.Code);
        store.Verify(
            s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingReservation_ReturnsNotFound()
    {
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryReservation?)null);
        var handler = new GetInventoryReservationQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryReservationQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Inventory.ReservationNotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_FoundReservation_QueriesById_AndMapsReadModel()
    {
        var reservation = CreateReservation(out var reservationId);
        InventoryReservationId? requestedId = null;
        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetReservationByIdAsync(It.IsAny<InventoryReservationId>(), It.IsAny<CancellationToken>()))
            .Callback<InventoryReservationId, CancellationToken>((id, _) => requestedId = id)
            .ReturnsAsync(reservation);
        var handler = new GetInventoryReservationQueryHandler(store.Object);

        var result = await handler.HandleAsync(
            new GetInventoryReservationQuery(reservationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reservationId, requestedId?.Value);

        var readModel = result.Value!;
        Assert.Equal(reservationId, readModel.ReservationId);
        Assert.Equal("SKU-1", readModel.Sku);
        Assert.Equal(3, readModel.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, readModel.Status);
        Assert.Equal(UtcNow, readModel.CreatedAtUtc);
    }

    private static InventoryReservation CreateReservation(out Guid reservationId)
    {
        var id = InventoryReservationId.Create();
        reservationId = id.Value;

        return InventoryReservation.Hydrate(
            id,
            OrderId.Create(),
            new InventorySku("SKU-1"),
            3,
            InventoryReservationStatus.Pending,
            UtcNow.AddMinutes(15),
            InventoryReservation.InitialVersion,
            UtcNow,
            UtcNow);
    }
}
