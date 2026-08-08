using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Exercises the complete authenticated Cart lifecycle end to end against real Redis:
/// create → retrieve → add → update → remove → add again → clear, plus Clear TTL semantics,
/// expiration, and a stale-version concurrency conflict through the existing CAS mechanism.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class AuthenticatedCartLifecycleRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public AuthenticatedCartLifecycleRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteAuthenticatedCartLifecycle_EndToEnd()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var get = new GetAuthenticatedCartQueryHandler(store, currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var update = new UpdateAuthenticatedCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var remove = new RemoveAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var clear = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        // 1-2. Create the authenticated Cart and verify its ownership.
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var createdState = created.Value!;
        Assert.Equal(userId, createdState.UserId);
        Assert.Empty(createdState.Items);
        Assert.Equal(1, createdState.Version);
        _fixture.Track(createdState);

        // 3. Retrieve the freshly created Cart through the user index.
        var initial = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(initial.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(createdState, initial.Value);

        // 4. Add two items.
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();
        var addA = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantA, Guid.NewGuid(), "SKU-A", "Product A", 10.00m, "USD", 2),
            CancellationToken.None);
        Assert.True(addA.IsSuccess);
        Assert.Equal(2, addA.Value!.Version);
        var addB = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantB, Guid.NewGuid(), "SKU-B", "Product B", 20.00m, "USD", 1),
            CancellationToken.None);
        Assert.True(addB.IsSuccess);
        Assert.Equal(3, addB.Value!.Version);
        Assert.Equal(2, addB.Value.Items.Count);

        // 5. Update quantity and verify.
        var updated = await update.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(variantA, 5),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal(5, Assert.Single(updated.Value!.Items, i => i.VariantId == new VariantId(variantA)).Quantity);
        Assert.Equal(4, updated.Value.Version);

        // 6. Remove one item and verify identity/ownership are preserved.
        var removed = await remove.HandleAsync(new RemoveAuthenticatedCartItemCommand(variantA), CancellationToken.None);
        Assert.True(removed.IsSuccess);
        Assert.Single(removed.Value!.Items);
        Assert.Equal(5, removed.Value.Version);
        Assert.Equal(createdState.CartId, removed.Value.CartId);
        Assert.Equal(userId, removed.Value.UserId);

        // 7. Add an item again.
        var reAdd = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantA, Guid.NewGuid(), "SKU-A", "Product A", 10.00m, "USD", 3),
            CancellationToken.None);
        Assert.True(reAdd.IsSuccess);
        Assert.Equal(2, reAdd.Value!.Items.Count);
        Assert.Equal(6, reAdd.Value.Version);

        // 8. Clear the Cart: all items removed, identity and ownership unchanged.
        var cleared = await clear.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        Assert.Empty(cleared.Value!.Items);
        Assert.Equal(7, cleared.Value.Version);
        Assert.Equal(createdState.CartId, cleared.Value.CartId);
        Assert.Equal(userId, cleared.Value.UserId);

        // 9. The cleared Cart is retrievable and matches the persisted state.
        var final = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(final.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(cleared.Value, final.Value);
    }

    [Fact]
    public async Task Clear_FollowsMutationTtlPolicy_RefreshingBothKeys()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var clear = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var cartId = created.Value!.CartId;
        var addResult = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addResult.IsSuccess);

        var cartKey = _fixture.CartKey(cartId);
        var userKey = _fixture.UserKey(userId);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromSeconds(10));
        await _fixture.Database.KeyExpireAsync(userKey, TimeSpan.FromSeconds(10));

        var result = await clear.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var cartTtl = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        var userTtl = await _fixture.Database.KeyTimeToLiveAsync(userKey);
        Assert.NotNull(cartTtl);
        Assert.NotNull(userTtl);
        Assert.True(cartTtl.Value > TimeSpan.FromSeconds(50) && cartTtl.Value <= TimeSpan.FromSeconds(60),
            $"A successful authenticated Clear must refresh the Cart TTL to ~60s, got {cartTtl.Value}");
        Assert.True(userTtl.Value > TimeSpan.FromSeconds(50) && userTtl.Value <= TimeSpan.FromSeconds(60),
            $"A successful authenticated Clear must refresh the user index TTL to ~60s, got {userTtl.Value}");
    }

    [Fact]
    public async Task AuthenticatedCart_Expires_AndBehavesAsMissing()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var state = created.Value!;
        _fixture.Track(state);

        var cartKey = _fixture.CartKey(state.CartId);
        var userKey = _fixture.UserKey(userId);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await _fixture.Database.KeyExpireAsync(userKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        var get = new GetAuthenticatedCartQueryHandler(store, currentUser);
        var expired = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(expired.IsFailure);
        Assert.Equal("Cart.CartNotFound", expired.Error.Code);
        Assert.Null(await store.GetByUserIdAsync(userId));

        // Expired Cart is not resurrected by mutations.
        var addResult = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 1m, "USD", 1),
            CancellationToken.None);
        Assert.True(addResult.IsFailure);
        Assert.Equal("Cart.CartNotFound", addResult.Error.Code);
    }

    [Fact]
    public async Task AuthenticatedMutation_WithStaleVersion_ReturnsConcurrencyConflict_AndDoesNotOverwrite()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
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

        // The handler reads the stale snapshot through a stale user index and attempts a stale write.
        var addHandler = new AddAuthenticatedCartItemCommandHandler(
            new StaleUserIndexStore(store, stale),
            FixedDateTimeProvider.CreateDefault(),
            currentUser);

        var result = await addHandler.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-STALE", "Stale Product", 1m, "USD", 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);

        // The stale write must not have overwritten the concurrent commit.
        var current = await store.GetAsync(stale.CartId);
        Assert.NotNull(current);
        RedisCartTestData.AssertCartStateEqual(concurrentState, current);
    }

    private sealed class StaleUserIndexStore : ICartStore
    {
        private readonly ICartStore _inner;
        private readonly CartState _stale;

        public StaleUserIndexStore(ICartStore inner, CartState stale)
        {
            _inner = inner;
            _stale = stale;
        }

        public Task<CartState?> GetAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(cartId, cancellationToken);

        public Task<CartState?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CartState?>(_stale);

        public Task<CartSaveResult> CreateForUserAsync(CartState cart, CancellationToken cancellationToken = default) =>
            _inner.CreateForUserAsync(cart, cancellationToken);

        public Task<CartSaveResult> SaveAsync(CartState cart, long expectedVersion, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(cart, expectedVersion, cancellationToken);

        public Task<CartSaveResult> MergeAndConsumeAsync(
            CartState target,
            long expectedVersion,
            long expectedSourceVersion,
            CartId sourceCartId,
            CancellationToken cancellationToken = default) =>
            _inner.MergeAndConsumeAsync(target, expectedVersion, expectedSourceVersion, sourceCartId, cancellationToken);

        public Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(cartId, cancellationToken);

        public Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(cartId, cancellationToken);
    }
}
