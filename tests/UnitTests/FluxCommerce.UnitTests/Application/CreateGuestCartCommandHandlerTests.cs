using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CreateGuestCartCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PersistsNewGuestCart_WithExpectedInitialState()
    {
        CartState? saved = null;
        var store = CreateStore(
            (state, _, _) => saved = state,
            (state, _, _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.NotEqual(Guid.Empty, saved!.CartId.Value);
        Assert.Null(saved.UserId);
        Assert.Equal(1, saved.Version);
        Assert.Empty(saved.Items);
        Assert.Equal(new DateTimeOffset(FixedDateTimeProvider.DefaultUtcNow), saved.CreatedAtUtc);
        Assert.Equal(new DateTimeOffset(FixedDateTimeProvider.DefaultUtcNow), saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheCreatedCartState()
    {
        CartState? saved = null;
        var store = CreateStore(
            (state, _, _) => saved = state,
            (state, _, _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(saved, result.Value);
    }

    [Fact]
    public async Task HandleAsync_GeneratesNewIdentifier_ForEachRequest()
    {
        var store = CreateStore(
            null,
            (state, _, _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var first = await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        var second = await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.NotEqual(first.Value!.CartId, second.Value!.CartId);
    }

    [Fact]
    public async Task HandleAsync_SavesWithExpectedVersionZero()
    {
        long? expectedVersion = null;
        var store = CreateStore(
            null,
            (state, version, _) =>
            {
                expectedVersion = version;
                return new CartSaveResult(CartSaveStatus.Success, state);
            });
        var handler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.Equal(0, expectedVersion);
    }

    [Fact]
    public async Task HandleAsync_WhenIdentifierCollides_ReturnsConflictError()
    {
        var store = CreateStore(
            null,
            (_, _, _) => new CartSaveResult(CartSaveStatus.VersionConflict, null));
        var handler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static ICartStore CreateStore(
        Action<CartState, long, CancellationToken>? onSave,
        Func<CartState, long, CancellationToken, CartSaveResult> returns)
    {
        var store = new Mock<ICartStore>();
        var setup = store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()));

        if (onSave is not null)
            setup.Callback(onSave);

        setup.ReturnsAsync(returns);
        return store.Object;
    }
}
