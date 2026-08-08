using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetGuestCartQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenGuestCartExists_ReturnsThePreservedCartState()
    {
        var state = RedisCartTestData.Create(userId: null, version: 4, quantity: 2);
        var store = CreateStore(state);
        var handler = new GetGuestCartQueryHandler(store);

        var result = await handler.HandleAsync(new GetGuestCartQuery(state.CartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(state.CartId, result.Value!.CartId);
        Assert.Null(result.Value.UserId);
        Assert.Equal(state.Version, result.Value.Version);
        Assert.Equal(state.Items, result.Value.Items);
        Assert.Equal(state.CreatedAtUtc, result.Value.CreatedAtUtc);
        Assert.Equal(state.UpdatedAtUtc, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_UsesTheGuestIdentifier_ToResolveCartIdentity()
    {
        var cartId = Guid.NewGuid();
        CartId? queried = null;
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .Callback<CartId, CancellationToken>((id, _) => queried = id)
            .ReturnsAsync(RedisCartTestData.Create(userId: null));
        var handler = new GetGuestCartQueryHandler(store.Object);

        await handler.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);

        Assert.Equal(new CartId(cartId), queried);
    }

    [Fact]
    public async Task HandleAsync_WhenCartIsMissing_ReturnsCartNotFound()
    {
        var store = CreateStore(null);
        var handler = new GetGuestCartQueryHandler(store);

        var result = await handler.HandleAsync(new GetGuestCartQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenResolvedCartIsAuthenticated_ReturnsAccessDenied()
    {
        var userId = Guid.NewGuid();
        var state = RedisCartTestData.Create(userId: userId);
        var store = CreateStore(state);
        var handler = new GetGuestCartQueryHandler(store);

        var result = await handler.HandleAsync(new GetGuestCartQuery(state.CartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyCartId_ReturnsValidationError()
    {
        var store = CreateStore(null);
        var handler = new GetGuestCartQueryHandler(store);

        var result = await handler.HandleAsync(new GetGuestCartQuery(Guid.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_DoesNotMutateTheStoredCart()
    {
        var state = RedisCartTestData.Create(userId: null, version: 3, quantity: 1);
        var store = CreateStore(state);
        var handler = new GetGuestCartQueryHandler(store);

        var before = new CartState(
            state.CartId,
            state.UserId,
            state.Version,
            state.CreatedAtUtc,
            state.UpdatedAtUtc,
            state.Items);

        await handler.HandleAsync(new GetGuestCartQuery(state.CartId.Value), CancellationToken.None);

        Assert.Equal(before, state);
    }

    private static ICartStore CreateStore(CartState? state)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return store.Object;
    }
}
