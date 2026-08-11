using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderLineItemTests
{
    private static OrderLineItem CreateValidItem() =>
        new(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku("ABC-1"),
            "Test Product",
            new OrderMoney(19.99m, "USD"),
            2);

    [Fact]
    public void Constructor_WithValidValues_Succeeds()
    {
        var item = CreateValidItem();

        Assert.NotNull(item.VariantId);
        Assert.NotNull(item.ProductId);
        Assert.Equal("ABC-1", item.Sku.Value);
        Assert.Equal("Test Product", item.ProductName);
        Assert.Equal(19.99m, item.UnitPrice.Amount);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public void Constructor_WithNullVariantId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderLineItem(OrderProductId.Create(), null!, new OrderSku("ABC-1"), "Test Product", new OrderMoney(19.99m, "USD"), 2));
    }

    [Fact]
    public void Constructor_WithNullProductId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderLineItem(null!, OrderVariantId.Create(), new OrderSku("ABC-1"), "Test Product", new OrderMoney(19.99m, "USD"), 2));
    }

    [Fact]
    public void Constructor_WithNullSku_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), null!, "Test Product", new OrderMoney(19.99m, "USD"), 2));
    }

    [Fact]
    public void Constructor_WithNullUnitPrice_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-1"), "Test Product", null!, 2));
    }

    [Fact]
    public void Constructor_WithBlankProductName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-1"), "   ", new OrderMoney(19.99m, "USD"), 2));
    }

    [Fact]
    public void Constructor_WithProductNameOverMax_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderLineItem(
                OrderProductId.Create(),
                OrderVariantId.Create(),
                new OrderSku("ABC-1"),
                new string('A', OrderLineItem.ProductNameMaxLength + 1),
                new OrderMoney(19.99m, "USD"),
                2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveQuantity_Throws(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku("ABC-1"), "Test Product", new OrderMoney(19.99m, "USD"), quantity));
    }

    [Fact]
    public void LineTotal_MultipliesUnitPriceByQuantity()
    {
        var item = CreateValidItem();

        Assert.Equal(39.98m, item.LineTotal.Amount);
        Assert.Equal("USD", item.LineTotal.Currency);
    }

    [Fact]
    public void EntityEquality_IsByIdentity()
    {
        var variantId = OrderVariantId.Create();

        var first = new OrderLineItem(OrderProductId.Create(), variantId, new OrderSku("ABC-1"), "Test Product", new OrderMoney(19.99m, "USD"), 2);
        var second = new OrderLineItem(OrderProductId.Create(), variantId, new OrderSku("ABC-1"), "Test Product", new OrderMoney(19.99m, "USD"), 2);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EntityEquality_DifferentVariantId_AreNotEqual()
    {
        var first = CreateValidItem();
        var second = CreateValidItem();

        Assert.NotEqual(first, second);
    }
}
