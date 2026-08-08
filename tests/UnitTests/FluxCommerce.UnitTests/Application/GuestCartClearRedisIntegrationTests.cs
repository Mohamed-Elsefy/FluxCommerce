using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Verifies Guest Cart Clear against real Redis: populated/empty/missing/expired carts,
/// cross-guest and authenticated-cart rejection with no mutation or TTL refresh, stale-version
/// protection and the mutation TTL policy.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class GuestCartClearRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public GuestCartClearRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Clear_PopulatedCart_EmptiesAndPersists_WithVersionBump()
    {
        var store = _fixture.CreateStore();
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await CreatePopulatedCartAsync();

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(created.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(4, result.Value.Version);
        Assert.Equal(created.CartId, result.Value.CartId);
        Assert.Null(result.Value.UserId);

        var persisted = await store.GetAsync(created.CartId);
        Assert.NotNull(persisted);
        Assert.Empty(persisted!.Items);
        Assert.Equal(4, persisted.Version);
    }

    [Fact]
    public async Task Clear_EmptyCart_IsNoOp_AndPersistsWithoutVersionChange()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(created.Value!.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(1, result.Value.Version);

        var persisted = await store.GetAsync(created.Value.CartId);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.Version);
    }

    [Fact]
    public async Task Clear_MissingCart_ReturnsCartNotFound_AndCreatesNothing()
    {
        var store = _fixture.CreateStore();
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var cartId = new CartId(Guid.NewGuid());

        var result = await clear.HandleAsync(new ClearGuestCartCommand(cartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.False(await store.ExistsAsync(cartId));
    }

    [Fact]
    public async Task Clear_ExpiredCart_BehavesAsMissing_AndDoesNotResurrect()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var cartKey = _fixture.CartKey(created.Value!.CartId);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(created.Value.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.False(await store.ExistsAsync(created.Value.CartId));
    }

    [Fact]
    public async Task Clear_WrongGuestIdentifier_IsRejected_WithoutAffectingOtherCart()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        // Guest B's cart, reachable only through Guest B's opaque identifier.
        var guestB = RedisCartTestData.Create(userId: null, quantity: 2);
        _fixture.Track(guestB);
        var saved = await store.SaveAsync(guestB, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, saved.Status);

        var guestBKey = _fixture.CartKey(guestB.CartId);
        var ttlBefore = await _fixture.Database.KeyTimeToLiveAsync(guestBKey);
        Assert.NotNull(ttlBefore);

        // Guest A guesses/uses a different identifier: Clear must fail without touching cart B.
        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(guestBKey);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter <= ttlBefore, "A rejected Clear must not refresh the TTL.");
        Assert.True(ttlBefore - ttlAfter < TimeSpan.FromSeconds(1), "TTL should only decay, not be reset.");

        var unchanged = await store.GetAsync(guestB.CartId);
        Assert.NotNull(unchanged);
        RedisCartTestData.AssertCartStateEqual(guestB, unchanged);
    }

    [Fact]
    public async Task Clear_AuthenticatedCart_IsRejected_WithoutMutationOrTtlRefresh()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var authenticated = RedisCartTestData.Create(userId: Guid.NewGuid(), quantity: 2);
        _fixture.Track(authenticated);
        var saved = await store.SaveAsync(authenticated, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, saved.Status);

        var cartKey = _fixture.CartKey(authenticated.CartId);
        var ttlBefore = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlBefore);

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(authenticated.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter <= ttlBefore, "A rejected Clear must not refresh the TTL.");
        Assert.True(ttlBefore - ttlAfter < TimeSpan.FromSeconds(1), "TTL should only decay, not be reset.");

        var unchanged = await store.GetAsync(authenticated.CartId);
        Assert.NotNull(unchanged);
        RedisCartTestData.AssertCartStateEqual(authenticated, unchanged);
    }

    [Fact]
    public async Task Clear_StaleSnapshot_ReturnsConcurrencyConflict_AndDoesNotOverwrite()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var stale = created.Value!;

        // A concurrent actor commits a newer version while our snapshot is stale.
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

        // The handler reads the stale snapshot and attempts a stale Clear.
        var clear = new ClearGuestCartCommandHandler(
            new StaleGetStore(store, stale),
            FixedDateTimeProvider.CreateDefault());

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(stale.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);

        var current = await store.GetAsync(stale.CartId);
        Assert.NotNull(current);
        RedisCartTestData.AssertCartStateEqual(concurrentState, current);
    }

    [Fact]
    public async Task Clear_FollowsMutationTtlPolicy_RefreshingTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var created = await CreatePopulatedCartAsync();
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var cartKey = _fixture.CartKey(created.CartId);

        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromSeconds(10));

        var result = await clear.HandleAsync(
            new ClearGuestCartCommand(created.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var ttl = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromSeconds(50) && ttl.Value <= TimeSpan.FromSeconds(60),
            $"A successful Clear must refresh the TTL to ~60s, got {ttl.Value}");
    }

    private async Task<CartState> CreatePopulatedCartAsync()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var add1 = await add.HandleAsync(
            new AddGuestCartItemCommand(created.Value!.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-1", "Product 1", 9.99m, "USD", 1),
            CancellationToken.None);
        Assert.True(add1.IsSuccess);
        var add2 = await add.HandleAsync(
            new AddGuestCartItemCommand(created.Value.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-2", "Product 2", 19.99m, "USD", 2),
            CancellationToken.None);
        Assert.True(add2.IsSuccess);
        Assert.Equal(3, add2.Value!.Version);

        return (await store.GetAsync(created.Value.CartId))!;
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
