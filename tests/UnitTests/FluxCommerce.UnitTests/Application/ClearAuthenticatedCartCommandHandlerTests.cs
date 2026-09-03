using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ClearAuthenticatedCartCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ClearsAllItems_PreservingIdentityAndOwnership()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 4, quantity: 2);
        var store = CreateStoreMock(loaded);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(loaded.CartId, result.Value.CartId);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(5, result.Value.Version);
        Assert.Equal(loaded.CreatedAtUtc, result.Value.CreatedAtUtc);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutPersisting()
    {
        var store = CreateStoreMock(RedisCartTestData.Create(userId: Guid.NewGuid()));
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var handler = new ClearAuthenticatedCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault(), currentUser);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingCart_ReturnsCartNotFound_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(loaded: null);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AnotherUsersCart_ReturnsAccessDenied_WithoutPersisting()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userB));
        var handler = CreateHandler(store, userA);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GuestCart_ReturnsAccessDenied_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: null));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 3);
        long? expectedVersion = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((_, version, _) => expectedVersion = version)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = CreateHandler(store, userId);

        await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.Equal(3, expectedVersion);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId, version: 2), saveStatus: CartSaveStatus.VersionConflict);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static ClearAuthenticatedCartCommandHandler CreateHandler(Mock<ICartStore> store, Guid userId) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault(), new FakeCurrentUser { UserId = userId, IsAuthenticated = true });

    private static Mock<ICartStore> CreateStoreMock(CartState? loaded, CartSaveStatus saveStatus = CartSaveStatus.Success)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(saveStatus, state));
        return store;
    }
}

