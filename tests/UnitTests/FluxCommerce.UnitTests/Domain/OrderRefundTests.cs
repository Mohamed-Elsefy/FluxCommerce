using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderRefundTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static Order CreateConfirmedOrder()
    {
        var order = Order.Place(
            OrderId.Create(),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            new OrderAddress("R", "L1", null, "C", "R", "1000", "US"),
            new OrderAddress("R", "L1", null, "C", "R", "1000", "US"),
            new[] { new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("SKU-1"), "P", new OrderMoney(10m, "USD"), 1) },
            UtcNow);
        // Confirm it
        var sagaConfirmed = order.Confirm(UtcNow.AddMinutes(1));
        Assert.True(sagaConfirmed.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        return order;
    }

    [Fact]
    public void CancelForRefund_FromConfirmed_ToCancelled_IsAllowed()
    {
        var order = CreateConfirmedOrder();
        var result = order.CancelForRefund(UtcNow.AddMinutes(2));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelForRefund_OnAlreadyCancelled_IsIdempotent()
    {
        var order = CreateConfirmedOrder();
        order.CancelForRefund(UtcNow.AddMinutes(2));
        var version = order.Version;
        var result = order.CancelForRefund(UtcNow.AddMinutes(3));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(version, order.Version);
    }

    [Fact]
    public void Cancel_WhenConfirmed_ShouldFail_ForApi()
    {
        var order = CreateConfirmedOrder();
        var result = order.Cancel(UtcNow.AddMinutes(2));
        Assert.True(result.IsFailure);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void CancelForRefund_FromPending_IsAllowed()
    {
        var order = Order.Place(
            OrderId.Create(),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            new OrderAddress("R", "L1", null, "C", "R", "1000", "US"),
            new OrderAddress("R", "L1", null, "C", "R", "1000", "US"),
            new[] { new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("SKU-2"), "P", new OrderMoney(5m, "USD"), 1) },
            UtcNow);
        var result = order.CancelForRefund(UtcNow.AddMinutes(1));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
