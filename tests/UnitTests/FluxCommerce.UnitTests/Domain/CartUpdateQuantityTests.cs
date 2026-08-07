using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartUpdateQuantityTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static (Cart Cart, VariantId VariantId, ProductId ProductId, Sku Sku, Money UnitPrice) CreateCartWithItem(
        int quantity = 1,
        string productName = "Product A")
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var variantId = VariantId.Create();
        var productId = ProductId.Create();
        var sku = new Sku("SKU-A");
        var unitPrice = new Money(19.99m, "USD");

        var result = cart.AddItem(productId, variantId, sku, productName, unitPrice, quantity, UtcNow);

        Assert.True(result.IsSuccess, result.Error.Code);

        return (cart, variantId, productId, sku, unitPrice);
    }

    [Fact]
    public void UpdateQuantity_ExistingVariant_ChangesQuantity()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem(quantity: 2);

        var result = cart.UpdateQuantity(variantId, 5, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public void UpdateQuantity_ExistingVariant_KeepsIdentity()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();

        var result = cart.UpdateQuantity(variantId, 7, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(variantId, Assert.Single(cart.Items).VariantId);
    }

    [Fact]
    public void UpdateQuantity_ExistingVariant_KeepsSnapshot()
    {
        var (cart, variantId, productId, sku, unitPrice) = CreateCartWithItem(productName: "Product A");

        var result = cart.UpdateQuantity(variantId, 7, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(cart.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(sku, item.Sku);
        Assert.Equal("Product A", item.ProductName);
        Assert.Equal(unitPrice, item.UnitPrice);
    }

    [Fact]
    public void UpdateQuantity_WithZero_ReturnsFailure()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();

        var result = cart.UpdateQuantity(variantId, 0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidQuantity, result.Error);
        Assert.Equal(1, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public void UpdateQuantity_WithNegative_ReturnsFailure()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();

        var result = cart.UpdateQuantity(variantId, -3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidQuantity, result.Error);
        Assert.Equal(1, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public void UpdateQuantity_UnknownVariant_ReturnsFailure()
    {
        var (cart, _, _, _, _) = CreateCartWithItem();

        var result = cart.UpdateQuantity(VariantId.Create(), 3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.CartItemNotFound, result.Error);
        Assert.Single(cart.Items);
    }

    [Fact]
    public void UpdateQuantity_UnknownVariant_LeavesCartUnchanged()
    {
        var (cart, _, _, _, _) = CreateCartWithItem();
        var version = cart.Version;

        var result = cart.UpdateQuantity(VariantId.Create(), 3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(version, cart.Version);
    }

    [Fact]
    public void UpdateQuantity_SameQuantity_ReturnsSuccessWithoutVersionChange()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem(quantity: 3);
        var version = cart.Version;

        var result = cart.UpdateQuantity(variantId, 3, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(version, cart.Version);
        Assert.Equal(3, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public void UpdateQuantity_WithChange_AdvancesVersion()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();
        var version = cart.Version;

        var result = cart.UpdateQuantity(variantId, 4, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(version + 1, cart.Version);
    }

    [Fact]
    public void UpdateQuantity_Success_UpdatesUpdatedAt()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();
        var later = UtcNow.AddMinutes(5);

        cart.UpdateQuantity(variantId, 4, later);

        Assert.Equal(later, cart.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateQuantity_DoesNotCreateNewItem()
    {
        var (cart, variantId, _, _, _) = CreateCartWithItem();

        var result = cart.UpdateQuantity(variantId, 6, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(cart.Items);
    }
}
