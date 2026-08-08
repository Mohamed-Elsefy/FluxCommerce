using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetAuthenticatedCartQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenAuthenticatedCartExists_ReturnsTheUsersCart()
    {
        var userId = Guid.NewGuid();
        var state = RedisCartTestData.Create(userId: userId, version: 4, quantity: 2);
        var store = CreateStore(state);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        RedisCartTestData.AssertCartStateEqual(state, result.Value);
    }

    [Fact]
    public async Task HandleAsync_ResolvesCartByTheCurrentUsersId()
    {
        var userId = Guid.NewGuid();
        Guid? queried = null;
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => queried = id)
            .ReturnsAsync(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.Equal(userId, queried);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired()
    {
        var store = CreateStore(null);
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var handler = new GetAuthenticatedCartQueryHandler(store.Object, currentUser);

        var result = await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasNoActiveCart_ReturnsCartNotFound()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(null);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_AnotherUsersCart_IsNotReturned()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var store = CreateStore(RedisCartTestData.Create(userId: userB));
        var handler = CreateHandler(store, userA);

        var result = await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_GuestCart_IsNotReturnedThroughAuthenticatedPath()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(RedisCartTestData.Create(userId: null));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_DoesNotMutateTheStoredCart()
    {
        var userId = Guid.NewGuid();
        var state = RedisCartTestData.Create(userId: userId, version: 3, quantity: 1);
        var before = state with { };
        var store = CreateStore(state);
        var handler = CreateHandler(store, userId);

        await handler.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);

        Assert.Equal(before, state);
    }

    private static GetAuthenticatedCartQueryHandler CreateHandler(Mock<ICartStore> store, Guid userId) =>
        new(store.Object, new FakeCurrentUser { UserId = userId, IsAuthenticated = true });

    private static Mock<ICartStore> CreateStore(CartState? state)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return store;
    }
}
