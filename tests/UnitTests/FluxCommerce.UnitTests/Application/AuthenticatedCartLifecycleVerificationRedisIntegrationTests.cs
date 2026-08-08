using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 3.5.3 verification: the complete authenticated Cart lifecycle exercised against real Redis
/// with a real retrieval after every mutation (create â†’ retrieve â†’ add â†’ retrieve â†’ update â†’
/// retrieve â†’ remove â†’ retrieve â†’ add again â†’ clear â†’ retrieve empty), plus a concurrent-mutation
/// test proving the existing optimistic-concurrency mechanism prevents lost updates when two
/// authenticated writers attempt to persist the same base version.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class AuthenticatedCartLifecycleVerificationRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public AuthenticatedCartLifecycleVerificationRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteAuthenticatedCartLifecycle_WithRetrievalAfterEveryStep()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var get = new GetAuthenticatedCartQueryHandler(store, currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var update = new UpdateAuthenticatedCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var remove = new RemoveAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var clear = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        // 1-2. Authenticated User A creates (or resolves) the Cart.
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var createdState = created.Value!;
        Assert.Equal(userId, createdState.UserId);
        Assert.Empty(createdState.Items);
        Assert.Equal(1, createdState.Version);
        _fixture.Track(createdState);

        // 3. Retrieve the Cart.
        var initial = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(initial.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(createdState, initial.Value);

        // 4. Add an item.
        var variantX = Guid.NewGuid();
        var added = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantX, 10.00m, "USD", 2),
            CancellationToken.None);
        Assert.True(added.IsSuccess);

        // 5. Retrieve and verify the item was persisted.
        var afterAdd = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(afterAdd.IsSuccess);
        var itemX = Assert.Single(afterAdd.Value!.Items, i => i.VariantId == new VariantId(variantX));
        Assert.Equal(2, itemX.Quantity);

        // 6. Update the quantity.
        var updated = await update.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(variantX, 5),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);

        // 7. Retrieve and verify the new quantity.
        var afterUpdate = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(afterUpdate.IsSuccess);
        Assert.Equal(5, Assert.Single(afterUpdate.Value!.Items, i => i.VariantId == new VariantId(variantX)).Quantity);

        // 8. Remove the item.
        var removed = await remove.HandleAsync(new RemoveAuthenticatedCartItemCommand(variantX), CancellationToken.None);
        Assert.True(removed.IsSuccess);

        // 9. Retrieve and verify the item is gone.
        var afterRemove = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(afterRemove.IsSuccess);
        Assert.Empty(afterRemove.Value!.Items);

        // 10. Add items again (two distinct variants).
        var variantY = Guid.NewGuid();
        var reAddX = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantX, 10.00m, "USD", 1),
            CancellationToken.None);
        Assert.True(reAddX.IsSuccess);
        var reAddY = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantY, 7.50m, "USD", 3),
            CancellationToken.None);
        Assert.True(reAddY.IsSuccess);
        Assert.Equal(2, reAddY.Value!.Items.Count);

        // 11. Clear the Cart.
        var cleared = await clear.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(cleared.IsSuccess);

        // 12. Retrieve and verify the Cart is empty; identity and ownership are preserved.
        var final = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(final.IsSuccess);
        Assert.Empty(final.Value!.Items);
        Assert.Equal(createdState.CartId, final.Value.CartId);
        Assert.Equal(userId, final.Value.UserId);
        RedisCartTestData.AssertCartStateEqual(cleared.Value, final.Value);
    }

    [Fact]
    public async Task ConcurrentAuthenticatedMutations_ExactlyOneWins_NoLostUpdate()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var baseVariant = Guid.NewGuid();
        var seeded = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(baseVariant, 1m, "USD", 1),
            CancellationToken.None);
        Assert.True(seeded.IsSuccess);

        // Both writers observe the same snapshot (Version 2) through a stale user index, then both
        // attempt to persist their own version of it at the same time.
        var stale = (await store.GetAsync(created.Value!.CartId))!;
        Assert.Equal(2, stale.Version);

        var writerA = new AddAuthenticatedCartItemCommandHandler(
            new StaleUserIndexStore(store, stale),
            FixedDateTimeProvider.CreateDefault(),
            currentUser,
            FakeCatalogVariantCartLookup.Available());
        var writerB = new AddAuthenticatedCartItemCommandHandler(
            new StaleUserIndexStore(store, stale),
            FixedDateTimeProvider.CreateDefault(),
            currentUser,
            FakeCatalogVariantCartLookup.Available());

        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();

        var results = await Task.WhenAll(
            writerA.HandleAsync(
                new AddAuthenticatedCartItemCommand(variantA, 2m, "USD", 1),
                CancellationToken.None),
            writerB.HandleAsync(
                new AddAuthenticatedCartItemCommand(variantB, 3m, "USD", 1),
                CancellationToken.None));

        // Exactly one mutation wins; the other reports the existing concurrency conflict.
        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(1, results.Count(r => r.IsFailure && r.Error!.Code == "Cart.CartConcurrencyConflict"));

        // The final persisted state reflects only the winner: the version is bumped by exactly one
        // and the loser's item never materialized.
        var current = (await store.GetAsync(stale.CartId))!;
        Assert.Equal(3, current.Version);
        var persistedVariants = current.Items.Select(i => i.VariantId.Value).ToHashSet();
        Assert.Contains(baseVariant, persistedVariants);
        Assert.True(
            persistedVariants.Contains(variantA) ^ persistedVariants.Contains(variantB),
            "Exactly one concurrent writer's item must be persisted; both items present means a lost update.");
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
