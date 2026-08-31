using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class UpdateGuestCartItemQuantityCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_UpdatesExistingItemQuantity_AndPersists()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2);
        CartState? saved = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, _, _) => saved = state)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value, 7),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, Assert.Single(result.Value!.Items).Quantity);
        Assert.Equal(2, result.Value.Version);
        Assert.NotNull(saved);
        Assert.Equal(7, Assert.Single(saved!.Items).Quantity);
    }

    [Fact]
    public async Task HandleAsync_InvalidQuantity_ReturnsValidationError_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2);
        var store = CreateStoreMock(loaded);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidQuantity", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsCartItemNotFound_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2);
        var store = CreateStoreMock(loaded);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, Guid.NewGuid(), 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartItemNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingCart_ReturnsCartNotFound_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(Guid.NewGuid(), Guid.NewGuid(), 3),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AuthenticatedCart_ReturnsAccessDenied_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid(), quantity: 2);
        var store = CreateStoreMock(loaded);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value, 5),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyVariantId_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: null);
        var store = CreateStoreMock(loaded);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, Guid.Empty, 3),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyCartId_ReturnsInvalidCartId_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(Guid.Empty, Guid.NewGuid(), 3),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 4);
        long? expectedVersion = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((_, version, _) => expectedVersion = version)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value, 6),
            CancellationToken.None);

        Assert.Equal(4, expectedVersion);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 4);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.VersionConflict);
        var handler = new UpdateGuestCartItemQuantityCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value, 6),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static Mock<ICartStore> CreateStoreMock(CartState? loaded, CartSaveStatus saveStatus = CartSaveStatus.Success)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(saveStatus, state));
        return store;
    }
}

