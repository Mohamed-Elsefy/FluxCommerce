using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CancelOrderCommandHandlerTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutQuerying()
    {
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var store = new Mock<IOrderStore>();
        var handler = CreateHandler(currentUser, store);

        var result = await handler.HandleAsync(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        store.Verify(
            s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingOrder_ReturnsNotFound_WithoutPersisting()
    {
        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        var handler = CreateHandler(CreateUser(), store);

        var result = await handler.HandleAsync(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.NotFound", result.Error.Code);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PendingOrder_CancelsAndPersists()
    {
        var customerId = CustomerId.Create();
        var order = OrderTestData.CreateOrder(OrderId.Create(), customerId, CreatedAtUtc);

        int saveChangesCalls = 0;
        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        store
            .Setup(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveChangesCalls++)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(new FakeCurrentUser { UserId = customerId.Value, IsAuthenticated = true }, store);

        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, saveChangesCalls);

        var details = result.Value!;
        Assert.Equal(order.Id.Value, details.OrderId);
        Assert.Equal("Cancelled", details.Status);
    }

    [Fact]
    public async Task HandleAsync_FinalizedOrder_ReturnsConflict_WithoutPersisting()
    {
        var customerId = CustomerId.Create();
        var orderId = OrderId.Create();
        var cancelled = OrderTestData.HydrateOrder(orderId, customerId, OrderStatus.Cancelled, CreatedAtUtc);

        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelled);
        var handler = CreateHandler(new FakeCurrentUser { UserId = customerId.Value, IsAuthenticated = true }, store);

        var result = await handler.HandleAsync(new CancelOrderCommand(orderId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Order.CancelConflict", result.Error.Code);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CancelOrderCommandHandler CreateHandler(FakeCurrentUser currentUser, Mock<IOrderStore> store) =>
        new(currentUser, FixedDateTimeProvider.CreateDefault(), store.Object);

    private static FakeCurrentUser CreateUser() =>
        new() { UserId = CustomerId.Create().Value, IsAuthenticated = true };
}
