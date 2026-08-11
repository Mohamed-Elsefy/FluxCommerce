using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrder;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetOrderQueryHandlerTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutQuerying()
    {
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var store = new Mock<IOrderStore>();
        var handler = new GetOrderQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetOrderQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        store.Verify(
            s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingOrder_ReturnsNotFound()
    {
        var currentUser = CreateUser();
        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        var handler = new GetOrderQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetOrderQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Order.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_FoundOrder_QueriesScopedToCurrentCustomer_AndMapsDetails()
    {
        var customerId = CustomerId.Create();
        var currentUser = new FakeCurrentUser { UserId = customerId.Value, IsAuthenticated = true };
        var orderId = OrderId.Create();
        var order = OrderTestData.CreateOrder(orderId, customerId, CreatedAtUtc);

        CustomerId? requestedCustomerId = null;
        OrderId? requestedOrderId = null;
        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetOwnedOrderAsync(It.IsAny<CustomerId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerId, OrderId, CancellationToken>((customer, orderId_, _) =>
            {
                requestedCustomerId = customer;
                requestedOrderId = orderId_;
            })
            .ReturnsAsync(order);
        var handler = new GetOrderQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetOrderQuery(orderId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(customerId, requestedCustomerId);
        Assert.Equal(orderId, requestedOrderId);

        var details = result.Value!;
        Assert.Equal(orderId.Value, details.OrderId);
        Assert.Equal(order.OrderNumber.Value, details.OrderNumber);
        Assert.Equal("Pending", details.Status);
        Assert.Equal(2, details.ItemCount);
        Assert.Equal(25.00m, details.Subtotal.Amount);
        Assert.Equal(25.00m, details.GrandTotal.Amount);
        Assert.Equal(2, details.LineItems.Count);
        Assert.Equal("Wireless Mouse", details.LineItems[0].ProductName);
        Assert.Equal("Keyboard", details.LineItems[1].ProductName);
        Assert.Equal("ORD-SUMMARY", details.ShippingAddress.RecipientName);
        Assert.Equal("ORD-SUMMARY", details.BillingAddress.RecipientName);
    }

    private static FakeCurrentUser CreateUser() =>
        new() { UserId = CustomerId.Create().Value, IsAuthenticated = true };
}

internal static class OrderTestData
{
    internal static Order CreateOrder(OrderId orderId, CustomerId customerId, DateTimeOffset createdAtUtc)
    {
        var shipping = new OrderAddress("ORD-SUMMARY", "1 Main St", null, "London", "Greater London", "SW1A 1AA", "GB");
        var billing = new OrderAddress("ORD-SUMMARY", "1 Main St", null, "London", "Greater London", "SW1A 1AA", "GB");

        var items = new[]
        {
            new OrderLineItem(
                OrderProductId.Create(),
                OrderVariantId.Create(),
                new OrderSku("SKU-1"),
                "Wireless Mouse",
                new OrderMoney(15.00m, "USD"),
                1),
            new OrderLineItem(
                OrderProductId.Create(),
                OrderVariantId.Create(),
                new OrderSku("SKU-2"),
                "Keyboard",
                new OrderMoney(10.00m, "USD"),
                1),
        };

        return Order.Place(orderId, customerId, new OrderNumber("ORD-1001"), shipping, billing, items, createdAtUtc);
    }

    internal static Order HydrateOrder(
        OrderId orderId,
        CustomerId customerId,
        OrderStatus status,
        DateTimeOffset createdAtUtc)
    {
        var shipping = new OrderAddress("ORD-SUMMARY", "1 Main St", null, "London", "Greater London", "SW1A 1AA", "GB");
        var billing = new OrderAddress("ORD-SUMMARY", "1 Main St", null, "London", "Greater London", "SW1A 1AA", "GB");

        return Order.Hydrate(
            orderId,
            customerId,
            new OrderNumber("ORD-1002"),
            shipping,
            billing,
            status,
            Order.InitialVersion + 1,
            createdAtUtc,
            createdAtUtc.AddMinutes(1),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku("SKU-1"),
                    "Wireless Mouse",
                    new OrderMoney(15.00m, "USD"),
                    1),
            ]);
    }
}
