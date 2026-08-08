using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AddAuthenticatedCartItemCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_AddsItemToAuthenticatedCart_AndPersists()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 2, quantity: 1);
        var store = CreateStoreMock(loaded);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-ADD", "Product A", 9.99m, "USD", 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(3, Assert.Single(result.Value.Items, i => i.Sku == new Sku("SKU-ADD")).Quantity);
        Assert.Equal(3, result.Value.Version);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateVariant_AccumulatesQuantityIntoSingleItem()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, quantity: 1);
        var store = CreateStoreMock(loaded);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(
            new AddAuthenticatedCartItemCommand(
                loaded.Items[0].VariantId.Value,
                loaded.Items[0].ProductId.Value,
                loaded.Items[0].Sku.Value,
                loaded.Items[0].ProductName,
                loaded.Items[0].UnitPrice.Amount,
                loaded.Items[0].UnitPrice.Currency,
                2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(loaded.Items[0].VariantId, item.VariantId);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutPersisting()
    {
        var store = CreateStoreMock(RedisCartTestData.Create(userId: Guid.NewGuid()));
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var handler = new AddAuthenticatedCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault(), currentUser);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

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

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

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

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

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

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InvalidQuantity_ReturnsValidationError_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(CreateCommand(quantity: 0), CancellationToken.None);

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

        var result = await handler.HandleAsync(CreateCommand(variantId: Guid.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_BlankProductName_ReturnsInvalidCartItem_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(CreateCommand(productName: "   "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PersistsWithExpectedVersionFromLoadedState()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 3);
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
        var handler = CreateHandler(store, userId);

        await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(3, expectedVersion);
        Assert.NotNull(saved);
        Assert.Equal(4, saved!.Version);
    }

    [Fact]
    public async Task HandleAsync_VersionConflict_ReturnsConflictError()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId, version: 2), saveStatus: CartSaveStatus.VersionConflict);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static AddAuthenticatedCartItemCommand CreateCommand(
        Guid? variantId = null,
        string productName = "Product A",
        int quantity = 1) =>
        new(
            variantId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-ADD",
            productName,
            9.99m,
            "USD",
            quantity);

    private static AddAuthenticatedCartItemCommandHandler CreateHandler(Mock<ICartStore> store, Guid userId) =>
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
