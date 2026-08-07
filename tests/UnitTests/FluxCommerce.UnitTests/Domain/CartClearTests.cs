using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartClearTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static Cart CreateGuestCart() => Cart.CreateGuest(CartId.Create(), UtcNow);

    private static void AddItem(Cart cart, int quantity = 1)
    {
        Assert.True(cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), quantity, UtcNow).IsSuccess);
    }

    [Fact]
    public void Clear_WithMultipleItems_EmptiesCart()
    {
        var cart = CreateGuestCart();
        AddItem(cart);
        AddItem(cart, quantity: 2);
        AddItem(cart, quantity: 3);

        var result = cart.Clear(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Clear_WithMultipleItems_RemovesAllItems()
    {
        var cart = CreateGuestCart();
        AddItem(cart);
        AddItem(cart);

        cart.Clear(UtcNow);

        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Clear_EmptyCart_ReturnsSuccess()
    {
        var cart = CreateGuestCart();

        var result = cart.Clear(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Clear_EmptyCart_DoesNotAdvanceVersion()
    {
        var cart = CreateGuestCart();
        var version = cart.Version;

        cart.Clear(UtcNow);

        Assert.Equal(version, cart.Version);
    }

    [Fact]
    public void Clear_AfterClearingAgain_IsNoOp()
    {
        var cart = CreateGuestCart();
        AddItem(cart);

        Assert.True(cart.Clear(UtcNow).IsSuccess);
        var version = cart.Version;
        var updatedAt = cart.UpdatedAtUtc;

        var result = cart.Clear(UtcNow.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Empty(cart.Items);
        Assert.Equal(version, cart.Version);
        Assert.Equal(updatedAt, cart.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_WithItems_AdvancesVersion()
    {
        var cart = CreateGuestCart();
        AddItem(cart);
        var version = cart.Version;

        var result = cart.Clear(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(version + 1, cart.Version);
    }

    [Fact]
    public void Clear_PreservesCartIdentity()
    {
        var cart = CreateGuestCart();
        var id = cart.Id;
        AddItem(cart);

        cart.Clear(UtcNow);

        Assert.Equal(id, cart.Id);
    }

    [Fact]
    public void Clear_PreservesGuestOwnership()
    {
        var cart = CreateGuestCart();
        AddItem(cart);

        cart.Clear(UtcNow);

        Assert.True(cart.IsGuest);
        Assert.False(cart.IsAuthenticated);
        Assert.Null(cart.UserId);
    }

    [Fact]
    public void Clear_PreservesAuthenticatedOwnership()
    {
        var userId = Guid.NewGuid();
        var cart = Cart.CreateAuthenticated(CartId.Create(), userId, UtcNow);

        Assert.True(cart.AddItem(ProductId.Create(), VariantId.Create(), new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow).IsSuccess);
        var result = cart.Clear(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(cart.Items);
        Assert.True(cart.IsAuthenticated);
        Assert.Equal(userId, cart.UserId);
    }

    [Fact]
    public void Clear_Success_UpdatesUpdatedAt()
    {
        var cart = CreateGuestCart();
        AddItem(cart);
        var later = UtcNow.AddMinutes(5);

        cart.Clear(later);

        Assert.Equal(later, cart.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_AfterClearing_CartRemainsUsable()
    {
        var cart = CreateGuestCart();
        AddItem(cart);
        var newVariant = VariantId.Create();

        Assert.True(cart.Clear(UtcNow).IsSuccess);
        var result = cart.AddItem(ProductId.Create(), newVariant, new Sku("SKU-B"), "Product B", new Money(9.99m, "USD"), 2, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(cart.Items);
        Assert.Equal(2, Assert.Single(cart.Items).Quantity);
    }
}
