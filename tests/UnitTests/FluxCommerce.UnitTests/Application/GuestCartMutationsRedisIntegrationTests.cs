using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

[Collection(RedisCartTestCollection.Name)]
public class GuestCartMutationsRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public GuestCartMutationsRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddGuestCartItem_PersistsAndRetrieves_ThroughApplicationHandlers()
    {
        var store = _fixture.CreateStore();
        var createHandler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var addHandler = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var getHandler = new GetGuestCartQueryHandler(store);

        var created = await createHandler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var variantId = Guid.NewGuid();
        var result = await addHandler.HandleAsync(
            new AddGuestCartItemCommand(
                created.Value!.CartId.Value,
                variantId,
                Guid.NewGuid(),
                "SKU-ADD",
                "Added Product",
                9.99m,
                "USD",
                3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value!.UserId);
        Assert.Equal(2, result.Value.Version);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(new VariantId(variantId), item.VariantId);
        Assert.Equal(3, item.Quantity);

        var retrieved = await getHandler.HandleAsync(
            new GetGuestCartQuery(created.Value.CartId.Value),
            CancellationToken.None);

        Assert.True(retrieved.IsSuccess);
        Assert.NotNull(retrieved.Value);
        RedisCartTestData.AssertCartStateEqual(result.Value, retrieved.Value);
    }

    [Fact]
    public async Task UpdateGuestCartItemQuantity_PersistsAndRetrieves_ThroughApplicationHandlers()
    {
        var store = _fixture.CreateStore();
        var createHandler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var addHandler = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var updateHandler = new UpdateGuestCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var created = await createHandler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var variantId = Guid.NewGuid();
        await addHandler.HandleAsync(
            new AddGuestCartItemCommand(created.Value!.CartId.Value, variantId, Guid.NewGuid(), "SKU-ADD", "Product A", 9.99m, "USD", 2),
            CancellationToken.None);

        var updated = await updateHandler.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(created.Value.CartId.Value, variantId, 8),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(8, Assert.Single(updated.Value!.Items).Quantity);
        Assert.Equal(3, updated.Value.Version);

        var current = await store.GetAsync(created.Value.CartId);
        Assert.NotNull(current);
        Assert.Equal(8, Assert.Single(current!.Items).Quantity);
        Assert.Equal(3, current.Version);
    }

    [Fact]
    public async Task RemoveGuestCartItem_PersistsAndRetrieves_ThroughApplicationHandlers()
    {
        var store = _fixture.CreateStore();
        var createHandler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var addHandler = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var removeHandler = new RemoveGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var created = await createHandler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var variantId = Guid.NewGuid();
        await addHandler.HandleAsync(
            new AddGuestCartItemCommand(created.Value!.CartId.Value, variantId, Guid.NewGuid(), "SKU-ADD", "Product A", 9.99m, "USD", 2),
            CancellationToken.None);

        var removed = await removeHandler.HandleAsync(
            new RemoveGuestCartItemCommand(created.Value.CartId.Value, variantId),
            CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.Empty(removed.Value!.Items);
        Assert.Equal(3, removed.Value.Version);

        var current = await store.GetAsync(created.Value.CartId);
        Assert.NotNull(current);
        Assert.Empty(current!.Items);
    }

    [Fact]
    public async Task GuestMutation_OnAuthenticatedCart_IsRejected_WithoutMutationOrTtlRefresh()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var authenticated = RedisCartTestData.Create(userId: userId);
        _fixture.Track(authenticated);
        var saved = await store.SaveAsync(authenticated, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, saved.Status);

        var key = _fixture.CartKey(authenticated.CartId);
        var ttlBefore = await _fixture.Database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttlBefore);

        var removeHandler = new RemoveGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var result = await removeHandler.HandleAsync(
            new RemoveGuestCartItemCommand(authenticated.CartId.Value, authenticated.Items[0].VariantId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter <= ttlBefore, "TTL must not be refreshed by a rejected mutation.");
        Assert.True(ttlBefore - ttlAfter < TimeSpan.FromSeconds(1), "TTL should only decay, not be reset.");

        var unchanged = await store.GetAsync(authenticated.CartId);
        Assert.NotNull(unchanged);
        RedisCartTestData.AssertCartStateEqual(authenticated, unchanged);
    }

    [Fact]
    public async Task GuestMutation_OnMissingCart_ReturnsCartNotFound_AndCreatesNothing()
    {
        var store = _fixture.CreateStore();
        var addHandler = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var cartId = new CartId(Guid.NewGuid());

        var result = await addHandler.HandleAsync(
            new AddGuestCartItemCommand(cartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 5m, "USD", 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.False(await store.ExistsAsync(cartId));
    }

    [Fact]
    public async Task GuestMutation_WithStaleSnapshot_ReturnsConcurrencyConflict_AndDoesNotOverwrite()
    {
        var store = _fixture.CreateStore();
        var createHandler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await createHandler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var stale = created.Value!;

        // Another actor commits a newer version while our snapshot is stale.
        var concurrent = CartApplicationMapper.ToAggregate(stale);
        var addResult = concurrent.AddItem(
            new ProductId(Guid.NewGuid()),
            new VariantId(Guid.NewGuid()),
            new Sku("SKU-CONC"),
            "Concurrent Product",
            new Money(5m, "USD"),
            1,
            new DateTimeOffset(FixedDateTimeProvider.DefaultUtcNow));
        Assert.True(addResult.IsSuccess);
        var concurrentState = CartApplicationMapper.ToState(concurrent);
        var bump = await store.SaveAsync(concurrentState, expectedVersion: stale.Version);
        Assert.Equal(CartSaveStatus.Success, bump.Status);

        // The handler reads the stale snapshot and attempts a stale write.
        var addHandler = new AddGuestCartItemCommandHandler(
            new StaleGetStore(store, stale),
            FixedDateTimeProvider.CreateDefault());

        var result = await addHandler.HandleAsync(
            new AddGuestCartItemCommand(stale.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-STALE", "Stale Product", 1m, "USD", 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);

        // The stale write must not have overwritten the concurrent commit.
        var current = await store.GetAsync(stale.CartId);
        Assert.NotNull(current);
        RedisCartTestData.AssertCartStateEqual(concurrentState, current);
    }

    private sealed class StaleGetStore : ICartStore
    {
        private readonly ICartStore _inner;
        private readonly CartState _stale;

        public StaleGetStore(ICartStore inner, CartState stale)
        {
            _inner = inner;
            _stale = stale;
        }

        public Task<CartState?> GetAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CartState?>(_stale);

        public Task<CartState?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            _inner.GetByUserIdAsync(userId, cancellationToken);

        public Task<CartSaveResult> CreateForUserAsync(CartState cart, CancellationToken cancellationToken = default) =>
            _inner.CreateForUserAsync(cart, cancellationToken);

        public Task<CartSaveResult> SaveAsync(CartState cart, long expectedVersion, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(cart, expectedVersion, cancellationToken);

        public Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(cartId, cancellationToken);

        public Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(cartId, cancellationToken);
    }
}
