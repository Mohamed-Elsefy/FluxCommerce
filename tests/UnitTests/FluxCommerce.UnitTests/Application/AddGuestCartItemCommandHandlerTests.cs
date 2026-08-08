using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AddGuestCartItemCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_AddsItemToEmptyGuestCart_AndPersists()
    {
        var loaded = CreateEmptyGuestCart();
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            CreateCommand(loaded.CartId.Value, quantity: 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(2, result.Value.Version);
        Assert.Null(result.Value.UserId);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateVariant_AccumulatesQuantityIntoSingleItem()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 1);
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            CreateCommand(loaded.CartId.Value, variantId: loaded.Items[0].VariantId.Value, quantity: 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(loaded.Items[0].VariantId, item.VariantId);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public async Task HandleAsync_ResolvesTheCartIdentifiedByTheCommand()
    {
        var loaded = CreateEmptyGuestCart();
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());
        var cartId = loaded.CartId.Value;
        CartId? queried = null;
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .Callback<CartId, CancellationToken>((id, _) => queried = id)
            .ReturnsAsync(loaded);

        await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.Equal(new CartId(cartId), queried);
    }

    [Fact]
    public async Task HandleAsync_InvalidQuantity_ReturnsValidationError_WithoutPersisting()
    {
        var loaded = CreateEmptyGuestCart();
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            CreateCommand(loaded.CartId.Value, quantity: 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidQuantity", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingCart_ReturnsCartNotFound_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AuthenticatedCart_ReturnsAccessDenied_WithoutPersisting()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid());
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(CreateCommand(loaded.CartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyCartId_ReturnsInvalidCartId_WithoutPersisting()
    {
        var store = CreateStoreMock(loaded: null);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(CreateCommand(Guid.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyVariantId_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var loaded = CreateEmptyGuestCart();
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            CreateCommand(loaded.CartId.Value, variantId: Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_BlankProductName_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var loaded = CreateEmptyGuestCart();
        var store = CreateStoreMock(loaded);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(
            CreateCommand(loaded.CartId.Value, productName: "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var loaded = CreateEmptyGuestCart(version: 3);
        CartState? saved = null;
        long? expectedVersion = null;
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, version, _) =>
            {
                saved = state;
                expectedVersion = version;
            })
            .ReturnsAsync((CartState state, long _, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        await handler.HandleAsync(CreateCommand(loaded.CartId.Value, quantity: 2), CancellationToken.None);

        Assert.Equal(3, expectedVersion);
        Assert.NotNull(saved);
        Assert.Equal(4, saved!.Version);
        Assert.Single(saved.Items);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var loaded = CreateEmptyGuestCart(version: 2);
        var store = CreateStoreMock(loaded, saveStatus: CartSaveStatus.VersionConflict);
        var handler = new AddGuestCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await handler.HandleAsync(CreateCommand(loaded.CartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static CartState CreateEmptyGuestCart(long version = 1) =>
        new(
            new CartId(Guid.NewGuid()),
            UserId: null,
            version,
            RedisCartTestData.FixedTime,
            RedisCartTestData.FixedTime,
            []);

    private static AddGuestCartItemCommand CreateCommand(
        Guid cartId,
        Guid? variantId = null,
        string sku = "SKU-ADD",
        string productName = "Product A",
        int quantity = 1) =>
        new(
            cartId,
            variantId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            sku,
            productName,
            9.99m,
            "USD",
            quantity);

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
