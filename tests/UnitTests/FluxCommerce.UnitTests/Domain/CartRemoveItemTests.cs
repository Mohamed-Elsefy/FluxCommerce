using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartRemoveItemTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static Cart CreateCartWithItems(out VariantId firstVariant, out VariantId secondVariant)
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        firstVariant = VariantId.Create();
        secondVariant = VariantId.Create();

        Assert.True(cart.AddItem(ProductId.Create(), firstVariant, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow).IsSuccess);
        Assert.True(cart.AddItem(ProductId.Create(), secondVariant, new Sku("SKU-B"), "Product B", new Money(9.99m, "USD"), 2, UtcNow).IsSuccess);

        return cart;
    }

    [Fact]
    public void RemoveItem_ExistingItem_RemovesItem()
    {
        var cart = CreateCartWithItems(out var firstVariant, out _);

        var result = cart.RemoveItem(firstVariant, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(cart.Items);
        Assert.False(cart.ContainsVariant(firstVariant));
    }

    [Fact]
    public void RemoveItem_ExistingItem_LeavesOtherItemsUnchanged()
    {
        var cart = CreateCartWithItems(out var firstVariant, out var secondVariant);

        var result = cart.RemoveItem(firstVariant, UtcNow);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(cart.Items);
        Assert.Equal(secondVariant, remaining.VariantId);
        Assert.Equal(2, remaining.Quantity);
    }

    [Fact]
    public void RemoveItem_UnknownVariant_ReturnsFailure()
    {
        var cart = CreateCartWithItems(out _, out _);
        var version = cart.Version;

        var result = cart.RemoveItem(VariantId.Create(), UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.CartItemNotFound, result.Error);
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(version, cart.Version);
    }

    [Fact]
    public void RemoveItem_Success_AdvancesVersion()
    {
        var cart = CreateCartWithItems(out var firstVariant, out _);
        var version = cart.Version;

        var result = cart.RemoveItem(firstVariant, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(version + 1, cart.Version);
    }

    [Fact]
    public void RemoveItem_Success_UpdatesUpdatedAt()
    {
        var cart = CreateCartWithItems(out var firstVariant, out _);
        var later = UtcNow.AddMinutes(5);

        cart.RemoveItem(firstVariant, later);

        Assert.Equal(later, cart.UpdatedAtUtc);
    }

    [Fact]
    public void RemoveItem_LastItem_LeavesCartEmptyAndValid()
    {
        var cart = CreateCartWithItems(out var firstVariant, out var secondVariant);

        Assert.True(cart.RemoveItem(firstVariant, UtcNow).IsSuccess);
        Assert.True(cart.RemoveItem(secondVariant, UtcNow).IsSuccess);

        Assert.Empty(cart.Items);
        Assert.True(cart.IsGuest);
    }

    [Fact]
    public void RemoveItem_AfterRemoval_CartRemainsUsable()
    {
        var cart = CreateCartWithItems(out var firstVariant, out _);
        var newVariant = VariantId.Create();

        Assert.True(cart.RemoveItem(firstVariant, UtcNow).IsSuccess);
        var result = cart.AddItem(ProductId.Create(), newVariant, new Sku("SKU-C"), "Product C", new Money(5.99m, "USD"), 1, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, cart.Items.Count);
        Assert.True(cart.ContainsVariant(newVariant));
    }
}
