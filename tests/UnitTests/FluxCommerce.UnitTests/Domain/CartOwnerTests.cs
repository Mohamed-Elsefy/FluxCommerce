using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartOwnerTests
{
    [Fact]
    public void Guest_IsGuest_True()
    {
        var owner = CartOwner.Guest();

        Assert.True(owner.IsGuest);
        Assert.False(owner.IsAuthenticated);
    }

    [Fact]
    public void Guest_HasNoUserId()
    {
        var owner = CartOwner.Guest();

        Assert.Null(owner.UserId);
    }

    [Fact]
    public void Authenticated_WithValidUserId_IsAuthenticated()
    {
        var userId = Guid.NewGuid();

        var owner = CartOwner.Authenticated(userId);

        Assert.True(owner.IsAuthenticated);
        Assert.False(owner.IsGuest);
        Assert.Equal(userId, owner.UserId);
    }

    [Fact]
    public void Authenticated_WithEmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CartOwner.Authenticated(Guid.Empty));
    }

    [Fact]
    public void Equality_Guest_And_Authenticated_AreNotEqual()
    {
        Assert.NotEqual(CartOwner.Guest(), CartOwner.Authenticated(Guid.NewGuid()));
    }

    [Fact]
    public void Equality_TwoGuests_AreEqual()
    {
        Assert.Equal(CartOwner.Guest(), CartOwner.Guest());
    }

    [Fact]
    public void Equality_Authenticated_TwoUsers_AreNotEqual()
    {
        Assert.NotEqual(CartOwner.Authenticated(Guid.NewGuid()), CartOwner.Authenticated(Guid.NewGuid()));
    }

    [Fact]
    public void Equality_Authenticated_SameUser_AreEqual()
    {
        var userId = Guid.NewGuid();

        Assert.Equal(CartOwner.Authenticated(userId), CartOwner.Authenticated(userId));
    }
}
