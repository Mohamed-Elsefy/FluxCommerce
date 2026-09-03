using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static OrderAddress CreateAddress() =>
        new("Jane Doe", "1 Main Street", null, "Springfield", "IL", "62701", "US");

    private static OrderLineItem CreateItem(string sku = "ABC-1", int quantity = 2, decimal price = 10m) =>
        new(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku(sku),
            "Test Product",
            new OrderMoney(price, "USD"),
            quantity);

    private static Order CreateOrder()
    {
        var items = new List<OrderLineItem> { CreateItem() };

        return Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-001"),
            CreateAddress(),
            CreateAddress(),
            items,
            UtcNow);
    }

    [Fact]
    public void Place_ReturnsPendingOrder()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Place_InitializesInitialVersion()
    {
        var order = CreateOrder();

        Assert.Equal(Order.InitialVersion, order.Version);
    }

    [Fact]
    public void Place_SetsCreationTimestamps()
    {
        var order = CreateOrder();

        Assert.Equal(UtcNow, order.CreatedAtUtc);
        Assert.Equal(UtcNow, order.UpdatedAtUtc);
    }

    [Fact]
    public void Place_WithEmptyLineItems_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Place(
                OrderId.Create(),
                CustomerId.Create(),
                new OrderNumber("ORD-001"),
                CreateAddress(),
                CreateAddress(),
                [],
                UtcNow));
    }

    [Fact]
    public void Place_WithNullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Order.Place(
                null!,
                CustomerId.Create(),
                new OrderNumber("ORD-001"),
                CreateAddress(),
                CreateAddress(),
                new List<OrderLineItem> { CreateItem() },
                UtcNow));
    }

    [Fact]
    public void Place_WithDefaultTimestamp_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Place(
                OrderId.Create(),
                CustomerId.Create(),
                new OrderNumber("ORD-001"),
                CreateAddress(),
                CreateAddress(),
                new List<OrderLineItem> { CreateItem() },
                default));
    }

    [Fact]
    public void Place_WithDuplicateVariants_Throws()
    {
        var variantId = OrderVariantId.Create();

        var first = new OrderLineItem(OrderProductId.Create(), variantId, new OrderSku("ABC-1"), "Test Product", new OrderMoney(10m, "USD"), 1);
        var second = new OrderLineItem(OrderProductId.Create(), variantId, new OrderSku("ABC-1"), "Test Product", new OrderMoney(10m, "USD"), 1);

        Assert.Throws<ArgumentException>(() =>
            Order.Place(
                OrderId.Create(),
                CustomerId.Create(),
                new OrderNumber("ORD-001"),
                CreateAddress(),
                CreateAddress(),
                new List<OrderLineItem> { first, second },
                UtcNow));
    }

    [Fact]
    public void Place_WithMixedCurrencies_Throws()
    {
        var first = new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-1"), "Test Product", new OrderMoney(10m, "USD"), 1);
        var second = new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-2"), "Test Product", new OrderMoney(10m, "EUR"), 1);

        Assert.Throws<ArgumentException>(() =>
            Order.Place(
                OrderId.Create(),
                CustomerId.Create(),
                new OrderNumber("ORD-001"),
                CreateAddress(),
                CreateAddress(),
                new List<OrderLineItem> { first, second },
                UtcNow));
    }

    [Fact]
    public void Subtotal_SumsLineTotals()
    {
        var order = Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-001"),
            CreateAddress(),
            CreateAddress(),
            new List<OrderLineItem> { CreateItem(quantity: 2, price: 10m), CreateItem(sku: "ABC-2", quantity: 3, price: 5m) },
            UtcNow);

        Assert.Equal(35m, order.Subtotal.Amount);
        Assert.Equal("USD", order.Subtotal.Currency);
    }

    [Fact]
    public void GrandTotal_IncludingShippingAndDiscount()
    {
        var order = CreateOrder();

        order.SetShippingCost(new OrderMoney(5m, "USD"), UtcNow);
        order.SetDiscount(new OrderMoney(2m, "USD"), UtcNow);

        Assert.Equal(20m, order.Subtotal.Amount);
        Assert.Equal(23m, order.GrandTotal.Amount);
    }

    [Fact]
    public void AddLineItem_WhenDuplicateVariant_ReturnsFailure()
    {
        var order = CreateOrder();
        var existing = order.LineItems.First();

        var result = order.AddLineItem(existing.ProductId, existing.VariantId, existing.Sku, existing.ProductName, existing.UnitPrice, 1, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.DuplicateVariant, result.Error);
    }

    [Fact]
    public void AddLineItem_WhenStatusIsNotPending_ReturnsFailure()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        var result = order.AddLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-2"), "Test Product", new OrderMoney(5m, "USD"), 1, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void AddLineItem_WithDifferentCurrency_ReturnsFailure()
    {
        var order = CreateOrder();

        var result = order.AddLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-2"), "Test Product", new OrderMoney(5m, "EUR"), 1, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void AddLineItem_WhenValid_AddsItem()
    {
        var order = CreateOrder();

        var result = order.AddLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-2"), "Test Product", new OrderMoney(5m, "USD"), 1, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, order.LineItems.Count);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void UpdateLineItemQuantity_WhenItemNotFound_ReturnsFailure()
    {
        var order = CreateOrder();

        var result = order.UpdateLineItemQuantity(OrderVariantId.Create(), 3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.LineItemNotFound, result.Error);
    }

    [Fact]
    public void UpdateLineItemQuantity_WhenValid_UpdatesItem()
    {
        var order = CreateOrder();
        var variantId = order.LineItems.First().VariantId;

        var result = order.UpdateLineItemQuantity(variantId, 3, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, order.LineItems.First().Quantity);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void UpdateLineItemQuantity_WhenQuantityUnchanged_ReturnsSuccessWithoutVersionBump()
    {
        var order = CreateOrder();
        var variantId = order.LineItems.First().VariantId;

        var result = order.UpdateLineItemQuantity(variantId, 2, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(Order.InitialVersion, order.Version);
    }

    [Fact]
    public void UpdateLineItemQuantity_WithInvalidQuantity_ReturnsFailure()
    {
        var order = CreateOrder();
        var variantId = order.LineItems.First().VariantId;

        var result = order.UpdateLineItemQuantity(variantId, 0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.InvalidQuantity, result.Error);
    }

    [Fact]
    public void RemoveLineItem_WhenItemNotFound_ReturnsFailure()
    {
        var order = CreateOrder();

        var result = order.RemoveLineItem(OrderVariantId.Create(), UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.LineItemNotFound, result.Error);
    }

    [Fact]
    public void RemoveLineItem_WhenValid_RemovesItem()
    {
        var order = CreateOrder();
        var variantId = order.LineItems.First().VariantId;

        var result = order.RemoveLineItem(variantId, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(order.LineItems);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void SetShippingCost_WithDifferentCurrency_ReturnsFailure()
    {
        var order = CreateOrder();

        var result = order.SetShippingCost(new OrderMoney(5m, "EUR"), UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void SetShippingCost_WhenValid_UpdatesCost()
    {
        var order = CreateOrder();

        var result = order.SetShippingCost(new OrderMoney(5m, "USD"), UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, order.ShippingCost.Amount);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void SetDiscount_WithDifferentCurrency_ReturnsFailure()
    {
        var order = CreateOrder();

        var result = order.SetDiscount(new OrderMoney(2m, "EUR"), UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var order = CreateOrder();

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ReturnsFailure()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Mutations_AfterCancellation_AreRejected()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);
        var variantId = order.LineItems.First().VariantId;

        Assert.True(order.AddLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-2"), "Test Product", new OrderMoney(5m, "USD"), 1, UtcNow).IsFailure);
        Assert.True(order.UpdateLineItemQuantity(variantId, 3, UtcNow).IsFailure);
        Assert.True(order.RemoveLineItem(variantId, UtcNow).IsFailure);
        Assert.True(order.SetShippingCost(new OrderMoney(5m, "USD"), UtcNow).IsFailure);
        Assert.True(order.SetDiscount(new OrderMoney(2m, "USD"), UtcNow).IsFailure);
    }

    [Fact]
    public void Cancel_AddsDomainEvent_OrderCancelled()
    {
        var order = CreateOrder();

        var result = order.Cancel(UtcNow);

        Assert.True(result.IsSuccess);
        var domainEvent = Assert.Single(order.GetDomainEvents().OfType<OrderCancelled>());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal(order.CustomerId, domainEvent.CustomerId);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_DoesNotRaiseDomainEvent()
    {
        var order = CreateOrder();
        order.Cancel(UtcNow);

        order.Cancel(UtcNow);

        Assert.Single(order.GetDomainEvents().OfType<OrderCancelled>());
    }

    [Fact]
    public void Place_AddsDomainEvent_OrderCreated()
    {
        var order = CreateOrder();

        var domainEvent = Assert.Single(order.GetDomainEvents().OfType<OrderCreated>());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal(order.CustomerId, domainEvent.CustomerId);
        Assert.Equal(order.OrderNumber, domainEvent.OrderNumber);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Place_DoesNotAddCancellationEvent()
    {
        var order = CreateOrder();

        Assert.Empty(order.GetDomainEvents().OfType<OrderCancelled>());
    }

    [Fact]
    public void Hydrate_ReconstitutesOrder()
    {
        var order = CreateOrder();
        order.SetShippingCost(new OrderMoney(5m, "USD"), UtcNow);

        var hydrated = Order.Hydrate(
            order.Id,
            order.CustomerId,
            order.OrderNumber,
            order.ShippingAddress,
            order.BillingAddress,
            order.Status,
            order.Version,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            order.LineItems);

        Assert.Equal(order.Id, hydrated.Id);
        Assert.Equal(order.CustomerId, hydrated.CustomerId);
        Assert.Equal(order.OrderNumber, hydrated.OrderNumber);
        Assert.Equal(order.Status, hydrated.Status);
        Assert.Equal(order.Version, hydrated.Version);
        Assert.Equal(order.CreatedAtUtc, hydrated.CreatedAtUtc);
        Assert.Equal(order.UpdatedAtUtc, hydrated.UpdatedAtUtc);
        Assert.Single(hydrated.LineItems);
    }

    [Fact]
    public void Hydrate_WithDefaultTimestamp_Throws()
    {
        var order = CreateOrder();

        Assert.Throws<ArgumentException>(() =>
            Order.Hydrate(
                order.Id,
                order.CustomerId,
                order.OrderNumber,
                order.ShippingAddress,
                order.BillingAddress,
                order.Status,
                order.Version,
                order.CreatedAtUtc,
                default,
                order.LineItems));
    }
}
