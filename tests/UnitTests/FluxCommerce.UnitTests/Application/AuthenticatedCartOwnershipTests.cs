using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AuthenticatedCartOwnershipTests
{
    // ---- ICurrentUser integration: authenticated identity resolution ----

    [Fact]
    public void ResolveAuthenticatedOwner_WithAuthenticatedUser_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };

        var result = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.True(result.Value.IsAuthenticated);
        Assert.False(result.Value.IsGuest);
    }

    [Fact]
    public void ResolveAuthenticatedOwner_Unauthenticated_ReturnsAuthenticationRequired()
    {
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };

        var result = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
    }

    [Fact]
    public void ResolveAuthenticatedOwner_IsAuthenticatedButNoUserId_ReturnsAuthenticationRequired()
    {
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = true };

        var result = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
    }

    [Fact]
    public void ResolveAuthenticatedOwner_EmptyUserId_IsNotTreatedAsAuthenticated()
    {
        var currentUser = new FakeCurrentUser { UserId = Guid.Empty, IsAuthenticated = true };

        var result = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
    }

    // ---- Authenticated Cart ownership stores the correct UserId ----

    [Fact]
    public void ValidateOwnership_OwnCart_Succeeds()
    {
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId);
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };

        var result = AuthenticatedCartOwnership.ValidateOwnership(cart, currentUser);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, cart.UserId);
    }

    [Fact]
    public void ValidateOwnership_AnotherUsersCart_FailsWithAccessDenied()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userB);
        var currentUser = new FakeCurrentUser { UserId = userA, IsAuthenticated = true };

        var result = AuthenticatedCartOwnership.ValidateOwnership(cart, currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public void ValidateOwnership_GuestCart_IsNotResolvableThroughAuthenticatedIdentity()
    {
        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), IsAuthenticated = true };
        var guestCart = RedisCartTestData.Create(userId: null);

        var result = AuthenticatedCartOwnership.ValidateOwnership(guestCart, currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public void ValidateOwnership_Unauthenticated_FailsWithAuthenticationRequired()
    {
        var cart = RedisCartTestData.Create(userId: Guid.NewGuid());
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };

        var result = AuthenticatedCartOwnership.ValidateOwnership(cart, currentUser);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
    }

    // ---- Ownership mismatch is rejected before mutation ----

    [Fact]
    public void OwnershipMismatch_IsRejected_BeforeAnyMutation()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userB, version: 4);
        var currentUser = new FakeCurrentUser { UserId = userA, IsAuthenticated = true };
        var mutated = false;

        var result = TryMutateOwnedCart(cart, currentUser, () => mutated = true);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        Assert.False(mutated, "The Domain mutation must never run for an ownership mismatch.");
        Assert.Equal(4, cart.Version);
    }

    [Fact]
    public void OwnedCart_AllowsMutation_AfterOwnershipValidation()
    {
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId, version: 4);
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var mutated = false;

        var result = TryMutateOwnedCart(cart, currentUser, () => mutated = true);

        Assert.True(result.IsSuccess);
        Assert.True(mutated);
    }

    // ---- Error mapping ----

    [Fact]
    public void AuthenticationRequired_Error_MapsToUnauthorized()
    {
        var error = CartErrorMapper.ToApplicationError(CartErrors.AuthenticationRequired);

        Assert.Equal(ErrorType.Unauthorized, error.Type);
        Assert.Equal("Cart.AuthenticationRequired", error.Code);
    }

    private static Result TryMutateOwnedCart(
        CartState cart,
        ICurrentUser currentUser,
        Action mutate)
    {
        var ownership = AuthenticatedCartOwnership.ValidateOwnership(cart, currentUser);

        if (ownership.IsFailure)
        {
            return ownership;
        }

        mutate();

        return Result.Success();
    }
}

