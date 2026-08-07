using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartAddItemTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static Cart CreateCart() => Cart.CreateGuest(CartId.Create(), UtcNow);

    private static Cart AddItem(
        Cart cart,
        VariantId? variantId = null,
        int quantity = 1,
        string sku = "SKU-A",
        string productName = "Product A",
        Money? unitPrice = null)
    {
        var result = cart.AddItem(
            ProductId.Create(),
            variantId ?? VariantId.Create(),
            new Sku(sku),
            productName,
            unitPrice ?? new Money(19.99m, "USD"),
            quantity,
            UtcNow);

        Assert.True(result.IsSuccess, result.Error.Code);

        return cart;
    }

    [Fact]
    public void AddItem_NewVariant_CreatesExactlyOneItem()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();

        var result = cart.AddItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(cart.Items);
        Assert.Equal(variantId, item.VariantId);
        Assert.True(cart.ContainsVariant(variantId));
    }

    [Fact]
    public void AddItem_NewVariant_StoresSnapshot()
    {
        var cart = CreateCart();
        var productId = ProductId.Create();
        var variantId = VariantId.Create();
        var unitPrice = new Money(24.5m, "EUR");

        var result = cart.AddItem(productId, variantId, new Sku("SKU-B"), "Product B", unitPrice, 3, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(cart.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal(new Sku("SKU-B"), item.Sku);
        Assert.Equal("Product B", item.ProductName);
        Assert.Equal(unitPrice, item.UnitPrice);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void AddItem_MultipleDifferentVariants_CreatesOneItemPerVariant()
    {
        var cart = CreateCart();

        AddItem(cart, quantity: 1);
        AddItem(cart, quantity: 2);

        Assert.Equal(2, cart.Items.Count);
    }

    [Fact]
    public void AddItem_SameVariantTwice_KeepsSingleItem()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();

        AddItem(cart, variantId: variantId, quantity: 1);
        AddItem(cart, variantId: variantId, quantity: 2);

        Assert.Single(cart.Items);
    }

    [Fact]
    public void AddItem_SameVariantTwice_AccumulatesQuantities()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();

        AddItem(cart, variantId: variantId, quantity: 1);
        AddItem(cart, variantId: variantId, quantity: 2);

        var item = Assert.Single(cart.Items);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void AddItem_ExistingVariant_DoesNotCreateSecondItem()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();

        AddItem(cart, variantId: variantId, quantity: 1);
        AddItem(cart, variantId: variantId, quantity: 1);

        Assert.Single(cart.Items);
    }

    [Fact]
    public void AddItem_ExistingVariant_KeepsOriginalSnapshot()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();
        var originalPrice = new Money(19.99m, "USD");

        AddItem(cart, variantId: variantId, quantity: 1, sku: "SKU-A", productName: "Product A", unitPrice: originalPrice);

        var result = cart.AddItem(ProductId.Create(), variantId, new Sku("SKU-Z"), "Product Z", new Money(5.00m, "EUR"), 2, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(cart.Items);
        Assert.Equal(originalPrice, item.UnitPrice);
        Assert.Equal("Product A", item.ProductName);
        Assert.Equal(new Sku("SKU-A"), item.Sku);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void AddItem_WithZeroQuantity_ReturnsFailure()
    {
        var cart = CreateCart();

        var result = cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidQuantity, result.Error);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void AddItem_WithNegativeQuantity_ReturnsFailure()
    {
        var cart = CreateCart();

        var result = cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), -1, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidQuantity, result.Error);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void AddItem_WithInvalidQuantity_LeavesCartUnchanged()
    {
        var cart = CreateCart();
        var version = cart.Version;
        var updatedAt = cart.UpdatedAtUtc;

        var result = cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 0, UtcNow.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Empty(cart.Items);
        Assert.Equal(version, cart.Version);
        Assert.Equal(updatedAt, cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_Success_AdvancesVersion()
    {
        var cart = CreateCart();
        var version = cart.Version;

        AddItem(cart);

        Assert.Equal(version + 1, cart.Version);
    }

    [Fact]
    public void AddItem_Success_UpdatesUpdatedAt()
    {
        var cart = CreateCart();
        var later = UtcNow.AddMinutes(5);

        cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, later);

        Assert.Equal(later, cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_SameSkuDifferentVariants_CreatesDistinctItems()
    {
        var cart = CreateCart();
        var firstVariant = VariantId.Create();
        var secondVariant = VariantId.Create();

        AddItem(cart, variantId: firstVariant, quantity: 1, sku: "SHARED-SKU");
        AddItem(cart, variantId: secondVariant, quantity: 1, sku: "SHARED-SKU");

        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(2, cart.Items.Select(item => item.VariantId).Distinct().Count());
        Assert.All(cart.Items, item => Assert.Equal(new Sku("SHARED-SKU"), item.Sku));
    }

    [Fact]
    public void AddItem_SameProductDifferentVariants_CreatesDistinctItems()
    {
        var cart = CreateCart();
        var productId = ProductId.Create();
        var firstVariant = VariantId.Create();
        var secondVariant = VariantId.Create();

        var first = cart.AddItem(productId, firstVariant, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);
        var second = cart.AddItem(productId, secondVariant, new Sku("SKU-B"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(2, cart.Items.Select(item => item.VariantId).Distinct().Count());
        Assert.All(cart.Items, item => Assert.Equal(productId, item.ProductId));
    }

    [Fact]
    public void AddItem_DuplicateThatOverflows_ReturnsFailure()
    {
        var cart = CreateCart();
        var variantId = VariantId.Create();

        AddItem(cart, variantId: variantId, quantity: int.MaxValue);

        var result = cart.AddItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidQuantity, result.Error);
        Assert.Equal(int.MaxValue, Assert.Single(cart.Items).Quantity);
    }
}
