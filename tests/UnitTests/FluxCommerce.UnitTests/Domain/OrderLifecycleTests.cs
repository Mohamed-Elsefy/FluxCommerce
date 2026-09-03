using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderLifecycleTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static OrderAddress CreateAddress() =>
        new("Jane Doe", "1 Main Street", null, "Springfield", "IL", "62701", "US");

    private static OrderLineItem CreateItem(string sku = "ABC-1") =>
        new(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku(sku),
            "Test Product",
            new OrderMoney(10m, "USD"),
            1);

    private static Order CreateOrder() =>
        Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-001"),
            CreateAddress(),
            CreateAddress(),
            [CreateItem()],
            UtcNow);

    // ---- Initial state ----

    [Fact]
    public void Place_CreatesOrderInPendingState()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    // ---- Allowed transitions ----

    [Fact]
    public void Cancel_PendingToCancelled_IsAllowed()
    {
        var order = CreateOrder();

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ---- Invalid transitions ----

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Fails()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ---- Cancellation protection ----

    [Fact]
    public void CancelledOrder_CannotBeModified()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        Assert.True(order.AddLineItem(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku("ABC-2"),
            "Test Product",
            new OrderMoney(5m, "USD"),
            1,
            UtcNow).IsFailure);

        Assert.True(order.UpdateLineItemQuantity(
            order.LineItems.First().VariantId,
            5,
            UtcNow).IsFailure);

        Assert.True(order.RemoveLineItem(
            order.LineItems.First().VariantId,
            UtcNow).IsFailure);

        Assert.True(order.SetShippingCost(new OrderMoney(3m, "USD"), UtcNow).IsFailure);
        Assert.True(order.SetDiscount(new OrderMoney(1m, "USD"), UtcNow).IsFailure);
    }

    [Fact]
    public void CancelledOrder_LineItemsRemainIntact()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        Assert.Single(order.LineItems);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ---- Version handling ----

    [Fact]
    public void Cancel_IncrementsVersion()
    {
        var order = CreateOrder();

        order.Cancel(UtcNow);

        Assert.Equal(2, order.Version);
        Assert.Equal(UtcNow, order.UpdatedAtUtc);
    }

    [Fact]
    public void FailedCancel_DoesNotIncrementVersion()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        order.Cancel(UtcNow);

        Assert.Equal(2, order.Version);
    }

    // ---- Domain events ----

    [Fact]
    public void Place_RaisesOrderCreated()
    {
        var order = CreateOrder();

        var domainEvent = Assert.Single(order.GetDomainEvents().OfType<OrderCreated>());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal(order.CustomerId, domainEvent.CustomerId);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Cancel_RaisesOrderCancelled()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsSuccess);
        var domainEvent = Assert.Single(order.GetDomainEvents().OfType<OrderCancelled>());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal(order.CustomerId, domainEvent.CustomerId);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void FailedCancel_DoesNotRaiseOrderCancelled()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);
        order.ClearDomainEvents();

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsFailure);
        Assert.Empty(order.GetDomainEvents().OfType<OrderCancelled>());
    }
}
