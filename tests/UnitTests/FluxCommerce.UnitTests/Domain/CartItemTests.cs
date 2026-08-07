using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartItemTests
{
    private static CartItem CreateItem(
        VariantId? variantId = null,
        ProductId? productId = null,
        string sku = "SKU-A",
        string productName = "Product A",
        Money? unitPrice = null,
        int quantity = 1)
    {
        return new CartItem(
            productId ?? ProductId.Create(),
            variantId ?? VariantId.Create(),
            new Sku(sku),
            productName,
            unitPrice ?? new Money(9.99m, "USD"),
            quantity);
    }

    [Fact]
    public void Constructor_WithValidSnapshot_StoresAllFields()
    {
        var variantId = VariantId.Create();
        var productId = ProductId.Create();
        var unitPrice = new Money(12.5m, "EUR");

        var item = new CartItem(productId, variantId, new Sku("SKU-A"), "Product A", unitPrice, 3);

        Assert.Equal(productId, item.ProductId);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal(new Sku("SKU-A"), item.Sku);
        Assert.Equal("Product A", item.ProductName);
        Assert.Equal(unitPrice, item.UnitPrice);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void VariantId_IsTheItemIdentity()
    {
        var variantId = VariantId.Create();

        var item = CreateItem(variantId: variantId);

        Assert.Equal(variantId, item.Id);
        Assert.Equal(variantId, item.VariantId);
    }

    [Fact]
    public void ProductId_And_VariantId_AreDistinctIdentities()
    {
        var item = CreateItem();

        Assert.NotEqual(item.ProductId.Value, item.VariantId.Value);
    }

    [Fact]
    public void Constructor_WithNullVariantId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CartItem(ProductId.Create(), null!, new Sku("SKU-A"), "Product A", new Money(9.99m, "USD"), 1));
    }

    [Fact]
    public void Constructor_WithNullProductId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CartItem(null!, VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(9.99m, "USD"), 1));
    }

    [Fact]
    public void Constructor_WithNullSku_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CartItem(ProductId.Create(), VariantId.Create(), null!, "Product A", new Money(9.99m, "USD"), 1));
    }

    [Fact]
    public void Constructor_WithBlankProductName_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateItem(productName: "   "));
    }

    [Fact]
    public void Constructor_WithTooLongProductName_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateItem(productName: new string('A', CartItem.ProductNameMaxLength + 1)));
    }

    [Fact]
    public void Constructor_WithNullUnitPrice_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CartItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", null!, 1));
    }

    [Fact]
    public void Constructor_WithZeroQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateItem(quantity: 0));
    }

    [Fact]
    public void Constructor_WithNegativeQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateItem(quantity: -1));
    }

    [Fact]
    public void DuplicateVariantId_CannotProduceInvalidCartState()
    {
        var variantId = VariantId.Create();

        var first = CreateItem(variantId: variantId, sku: "SKU-A", productName: "Product A", unitPrice: new Money(9.99m, "USD"), quantity: 1);
        var second = CreateItem(variantId: variantId, sku: "SKU-B", productName: "Product B", unitPrice: new Money(4.99m, "USD"), quantity: 2);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Items_WithDifferentVariantIds_AreNotEqual()
    {
        var first = CreateItem();
        var second = CreateItem();

        Assert.NotEqual(first, second);
    }
}
