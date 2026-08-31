using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ClearGuestCartCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PopulatedCart_EmptiesItems_AndPersists()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 2);
        CartState? saved = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, _, _) => saved = state)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(loaded.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(3, result.Value.Version);
        Assert.Equal(loaded.CartId, result.Value.CartId);
        Assert.Null(result.Value.UserId);
        Assert.Equal(loaded.CreatedAtUtc, result.Value.CreatedAtUtc);
        Assert.NotNull(saved);
        Assert.Empty(saved!.Items);
        Assert.Equal(3, saved.Version);
        Assert.Equal(loaded.CartId, saved.CartId);
    }

    [Fact]
    public async Task HandleAsync_EmptyCart_ReturnsSuccess_WithoutVersionChange_ButPersists()
    {
        var empty = RedisCartTestData.Create(userId: null, quantity: 2) with { Items = [] };
        CartState? saved = null;
        var store = CreateStoreMock(empty);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, _, _) => saved = state)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(empty.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(1, result.Value.Version);
        Assert.NotNull(saved);
        Assert.Equal(1, saved!.Version);
    }

    [Fact]
    public async Task HandleAsync_MissingCart_ReturnsCartNotFound_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(Guid.NewGuid()),
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
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(loaded.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyCartId_ReturnsInvalidCartId_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 7);
        long? expectedVersion = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((_, version, _) => expectedVersion = version)
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        await handler.HandleAsync(
            new ClearGuestCartCommand(loaded.CartId.Value),
            CancellationToken.None);

        Assert.Equal(7, expectedVersion);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 5);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.VersionConflict);
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(loaded.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CartMissingSaveStatus_ReturnsCartNotFound()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, version: 5);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.CartMissing);
        var handler = new ClearGuestCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            new ClearGuestCartCommand(loaded.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
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

