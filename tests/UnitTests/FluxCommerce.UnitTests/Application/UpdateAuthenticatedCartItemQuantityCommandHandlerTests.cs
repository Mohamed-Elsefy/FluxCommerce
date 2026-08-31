using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class UpdateAuthenticatedCartItemQuantityCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_UpdatesQuantity_AndPersists()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 2, quantity: 1);
        var store = CreateStoreMock(loaded);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(loaded.Items[0].VariantId.Value, 8),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, Assert.Single(result.Value!.Items).Quantity);
        Assert.Equal(3, result.Value.Version);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutPersisting()
    {
        var store = CreateStoreMock(RedisCartTestData.Create(userId: Guid.NewGuid()));
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var handler = new UpdateAuthenticatedCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault(), currentUser);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 5),
            CancellationToken.None);

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

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 5),
            CancellationToken.None);

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

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 5),
            CancellationToken.None);

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

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsCartItemNotFound_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartItemNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InvalidQuantity_ReturnsValidationError_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidQuantity", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyVariantId_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.Empty, 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 2);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.VersionConflict);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(loaded.Items[0].VariantId.Value, 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static UpdateAuthenticatedCartItemQuantityCommandHandler CreateHandler(Mock<ICartStore> store, Guid userId) =>
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

