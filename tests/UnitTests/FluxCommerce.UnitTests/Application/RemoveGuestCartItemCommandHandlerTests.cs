using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class RemoveGuestCartItemCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_RemovesExistingItem_AndPersists()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2);
        CartState? saved = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, _, _) => saved = state)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(2, result.Value.Version);
        Assert.NotNull(saved);
        Assert.Empty(saved!.Items);
    }

    [Fact]
    public async Task HandleAsync_MissingItem_ReturnsCartItemNotFound_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2);
        var store = CreateStoreMock(loaded);
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, Guid.NewGuid()),
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
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(Guid.NewGuid(), Guid.NewGuid()),
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
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyCartId_ReturnsInvalidCartId_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(Guid.Empty, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyVariantId_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: null);
        var store = CreateStoreMock(loaded);
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 5);
        long? expectedVersion = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((_, version, _) => expectedVersion = version)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value),
            CancellationToken.None);

        Assert.Equal(5, expectedVersion);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 5);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.VersionConflict);
        var handler = new RemoveGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new RemoveGuestCartItemCommand(loaded.CartId.Value, loaded.Items[0].VariantId.Value),
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

