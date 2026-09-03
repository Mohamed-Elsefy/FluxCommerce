using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AddAuthenticatedCartItemCommandHandlerTests
{
    private static readonly Guid VariantA = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_AddsItemToAuthenticatedCart_AndPersists()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId, version: 2, quantity: 1);
        var store = CreateStoreMock(loaded);
        var handler = CreateHandler(store, userId);
        var variantId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantId, 9.99m, "USD", 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Equal(2, result.Value.Items.Count);
        var item = Assert.Single(result.Value.Items, i => i.VariantId == new VariantId(variantId));
        Assert.Equal(3, item.Quantity);
        Assert.Equal(3, result.Value.Version);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

[Fact]
    public async Task HandleAsync_SnapshotComesFromCatalogContract_NotFromCaller()
    {
        var userId = Guid.NewGuid();
        // Create an empty authenticated cart directly to avoid seeded items.
        var emptyCart = new CartState(
            new CartId(Guid.NewGuid()),
            userId,
            1,
            RedisCartTestData.FixedTime,
            RedisCartTestData.FixedTime,
            []);
        var store = CreateStoreMock(emptyCart);
        var variantId = Guid.NewGuid();
        var lookup = new FakeCatalogVariantCartLookup(snapshots: new Dictionary<Guid, CatalogVariantEligibility>
        {
            [variantId] = new CatalogVariantEligibility(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "CATALOG-SKU",
                ProductName: "Catalog Product Name"),
        });
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            lookup);

        var result = await handler.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantId, 9.99m, "USD", 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(new VariantId(variantId), item.VariantId);
        // The snapshot persisted with the CartItem is exactly what the Catalog contract returned;
        // the caller has no way to influence the product identity, SKU or product name.
        Assert.Equal("CATALOG-SKU", item.Sku.Value);
        Assert.Equal("Catalog Product Name", item.ProductName);
        Assert.NotEqual(item.ProductId.Value, variantId);
    }

    [Fact]
    public async Task HandleAsync_MalformedCatalogResult_ReturnsCatalogOperationFailed_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            new FakeCatalogVariantCartLookup(emptyResults: true));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("Cart.CatalogOperationFailed", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ResolvesTheVariantThroughASingleBatchCall()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var lookup = FakeCatalogVariantCartLookup.Available();
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            lookup);
        var variantId = Guid.NewGuid();

        await handler.HandleAsync(new AddAuthenticatedCartItemCommand(variantId, 5m, "USD", 1), CancellationToken.None);

        Assert.Equal(1, lookup.CallCount);
        Assert.Equal(new[] { variantId }, lookup.LastVariantIds);
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
        var handler = new AddAuthenticatedCartItemCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

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

    [Fact]
    public async Task HandleAsync_MissingVariant_ReturnsVariantNotFound_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            new FakeCatalogVariantCartLookup(missing: [VariantA]));

        var result = await handler.HandleAsync(CreateCommand(variantId: VariantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.VariantNotFound", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnavailableVariant_ReturnsVariantUnavailable_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            new FakeCatalogVariantCartLookup(unavailable: [VariantA]));

        var result = await handler.HandleAsync(CreateCommand(variantId: VariantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.VariantUnavailable", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CatalogLookupFails_ReturnsCatalogOperationFailed_WithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var store = CreateStoreMock(RedisCartTestData.Create(userId: userId));
        var handler = new AddAuthenticatedCartItemCommandHandler(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true },
            new FakeCatalogVariantCartLookup(exception: new InvalidOperationException("Catalog is down.")));

        var result = await handler.HandleAsync(CreateCommand(variantId: VariantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("Cart.CatalogOperationFailed", result.Error.Code);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AddAuthenticatedCartItemCommand CreateCommand(
        Guid? variantId = null,
        int quantity = 1) =>
        new(
            variantId ?? Guid.NewGuid(),
            9.99m,
            "USD",
            quantity);

    private static AddAuthenticatedCartItemCommandHandler CreateHandler(Mock<ICartStore> store, Guid userId) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault(), new FakeCurrentUser { UserId = userId, IsAuthenticated = true }, FakeCatalogVariantCartLookup.Available());

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

