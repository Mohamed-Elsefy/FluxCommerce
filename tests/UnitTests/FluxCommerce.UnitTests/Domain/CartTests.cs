using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateGuest_ReturnsCartWithGuestOwnership()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        Assert.True(cart.IsGuest);
        Assert.False(cart.IsAuthenticated);
        Assert.Null(cart.UserId);
    }

    [Fact]
    public void CreateGuest_InitializesEmptyItemCollection()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        Assert.NotNull(cart.Items);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void CreateGuest_InitializesInitialVersion()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        Assert.Equal(Cart.InitialVersion, cart.Version);
    }

    [Fact]
    public void CreateGuest_SetsCreationTimestamps()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        Assert.Equal(UtcNow, cart.CreatedAtUtc);
        Assert.Equal(UtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void CreateGuest_SetsCartIdentifier()
    {
        var cartId = CartId.Create();

        var cart = Cart.CreateGuest(cartId, UtcNow);

        Assert.Equal(cartId, cart.Id);
    }

    [Fact]
    public void CreateAuthenticated_ReturnsCartWithAuthenticatedOwnership()
    {
        var userId = Guid.NewGuid();

        var cart = Cart.CreateAuthenticated(CartId.Create(), userId, UtcNow);

        Assert.True(cart.IsAuthenticated);
        Assert.False(cart.IsGuest);
        Assert.Equal(userId, cart.UserId);
    }

    [Fact]
    public void CreateAuthenticated_WithEmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Cart.CreateAuthenticated(CartId.Create(), Guid.Empty, UtcNow));
    }

    [Fact]
    public void CreateGuest_WithNullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Cart.CreateGuest(null!, UtcNow));
    }

    [Fact]
    public void CreateAuthenticated_WithNullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Cart.CreateAuthenticated(null!, Guid.NewGuid(), UtcNow));
    }

    [Fact]
    public void CreateGuest_WithDefaultTimestamp_Throws()
    {
        Assert.Throws<ArgumentException>(() => Cart.CreateGuest(CartId.Create(), default));
    }

    [Fact]
    public void ContainsVariant_WhenCartIsEmpty_ReturnsFalse()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        Assert.False(cart.ContainsVariant(VariantId.Create()));
    }

    [Fact]
    public void GuestCart_HasGuestOwner()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);

        var owner = cart.Owner;

        Assert.True(owner.IsGuest);
        Assert.Null(owner.UserId);
    }

    [Fact]
    public void AuthenticatedCart_HasAuthenticatedOwner()
    {
        var userId = Guid.NewGuid();

        var cart = Cart.CreateAuthenticated(CartId.Create(), userId, UtcNow);

        var owner = cart.Owner;

        Assert.True(owner.IsAuthenticated);
        Assert.Equal(userId, owner.UserId);
    }
}
