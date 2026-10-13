using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies optimistic concurrency against real Redis. The RedisCartStore commits each Cart
/// mutation through a single atomic Lua EVAL (compare-and-set on the stored version), so
/// stale writers are rejected with CartSaveStatus.VersionConflict and no lost update, no
/// double version increment and no unintended TTL refresh can occur. These tests observe the
/// resulting persistence behavior - they do not re-implement the script's logic.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartConcurrencyIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartConcurrencyIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TwoClients_RacingOnSameVersion_OneWins_NoLostUpdate()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1, quantity: 2);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);

        // Two logical clients both read version 1.
        var clientA = await store.GetAsync(cartV1.CartId);
        var clientB = await store.GetAsync(cartV1.CartId);
        Assert.NotNull(clientA);
        Assert.NotNull(clientB);
        Assert.Equal(1, clientA!.Version);
        Assert.Equal(1, clientB!.Version);

        // Client A performs a mutation with expectedVersion = 1.
        var mutationA = clientA with { Version = 2, Items = [clientA.Items[0] with { Quantity = 5 }] };
        var resultA = await store.SaveAsync(mutationA, expectedVersion: 1);
        Assert.Equal(CartSaveStatus.Success, resultA.Status);

        // Client B performs a mutation with the same stale expectedVersion = 1.
        var mutationB = clientB with { Version = 2, Items = [clientB.Items[0] with { Quantity = 9 }] };
        var resultB = await store.SaveAsync(mutationB, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.VersionConflict, resultB.Status);
        Assert.Null(resultB.Cart);

        // No lost update: the final Redis state contains Client A's successful mutation.
        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Version);
        Assert.Equal(5, loaded.Items[0].Quantity);
        Assert.NotEqual(9, loaded.Items[0].Quantity);

        var raw = (await _fixture.Database.StringGetAsync(cartKey)).ToString();
        Assert.Contains("\"quantity\":5", raw);
        Assert.DoesNotContain("\"quantity\":9", raw);
        Assert.Contains("\"version\":2", raw);
    }

    [Fact]
    public async Task ConcurrentMutations_WithSameExpectedVersion_ExactlyOneSucceeds_StateIsConsistent()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1, quantity: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);

        const int writers = 16;
        var tasks = new List<Task<CartSaveResult>>();
        var quantities = new List<int>();

        for (var i = 0; i < writers; i++)
        {
            var quantity = 100 + i;
            quantities.Add(quantity);

            var candidate = cartV1 with
            {
                Version = 2,
                Items = [cartV1.Items[0] with { Quantity = quantity }],
            };

            tasks.Add(store.SaveAsync(candidate, expectedVersion: 1));
        }

        var results = await Task.WhenAll(tasks);

        // Atomic CAS guarantees exactly one writer wins and every other writer gets a conflict.
        Assert.Equal(1, results.Count(r => r.Status == CartSaveStatus.Success));
        Assert.Equal(writers - 1, results.Count(r => r.Status == CartSaveStatus.VersionConflict));
        Assert.DoesNotContain(results, r => r.Status == CartSaveStatus.CartMissing);

        // The final version is exactly one above the starting version (no double increment)
        // and the persisted state matches exactly one writer's payload.
        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Version);
        Assert.Contains(loaded.Items[0].Quantity, quantities);
    }

    [Fact]
    public async Task ConcurrentMutations_OnDifferentCarts_AllSucceed_AndRemainIsolated()
    {
        var store = _fixture.CreateStore();
        var cartA = RedisCartTestData.Create(version: 1);
        var cartB = RedisCartTestData.Create(version: 1);
        _fixture.Track(_fixture.CartKey(cartA.CartId));
        _fixture.Track(_fixture.CartKey(cartB.CartId));

        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);

        var a2 = cartA with { Version = 2, Items = [cartA.Items[0] with { Quantity = 7 }] };
        var b2 = cartB with { Version = 2, Items = [cartB.Items[0] with { Quantity = 8 }] };

        var results = await Task.WhenAll(
            store.SaveAsync(a2, expectedVersion: 1),
            store.SaveAsync(b2, expectedVersion: 1));

        Assert.All(results, r => Assert.Equal(CartSaveStatus.Success, r.Status));

        var loadedA = await store.GetAsync(cartA.CartId);
        var loadedB = await store.GetAsync(cartB.CartId);
        Assert.Equal(2, loadedA!.Version);
        Assert.Equal(2, loadedB!.Version);
        Assert.Equal(7, loadedA.Items[0].Quantity);
        Assert.Equal(8, loadedB.Items[0].Quantity);
    }

    [Fact]
    public async Task StaleAttempt_DoesNotModifyState_OrIncrementVersion()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1, quantity: 2);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);

        var cartV2 = cartV1 with { Version = 2, Items = [cartV1.Items[0] with { Quantity = 5 }] };
        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(cartV2, expectedVersion: 1)).Status);

        // A stale write based on version 1 must be rejected.
        var stale = cartV1 with { Version = 3, Items = [cartV1.Items[0] with { Quantity = 99 }] };
        var result = await store.SaveAsync(stale, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.VersionConflict, result.Status);
        Assert.Null(result.Cart);

        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
        Assert.Equal(5, loaded.Items[0].Quantity);

        var raw = (await _fixture.Database.StringGetAsync(cartKey)).ToString();
        Assert.Contains("\"version\":2", raw);
        Assert.DoesNotContain("\"quantity\":99", raw);
    }

    [Fact]
    public async Task StaleAttempt_DoesNotRefreshTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromSeconds(10));

        // Expected version 0 against an existing cart at version 1 is stale.
        var stale = cartV1 with { Version = 2 };
        var result = await store.SaveAsync(stale, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.VersionConflict, result.Status);

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter.Value <= TimeSpan.FromSeconds(30),
            $"A stale write must not refresh the TTL (TTL was {ttlAfter.Value})");
    }

    [Fact]
    public async Task StaleAttempt_DoesNotRefreshTheUserIndexKeyTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var cartV1 = RedisCartTestData.Create(userId: userId, version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        var userKey = _fixture.UserKey(userId);
        _fixture.Track(cartKey);
        _fixture.Track(userKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromSeconds(10));
        await _fixture.Database.KeyExpireAsync(userKey, TimeSpan.FromSeconds(10));

        var stale = cartV1 with { Version = 2 };
        Assert.Equal(CartSaveStatus.VersionConflict, (await store.SaveAsync(stale, expectedVersion: 0)).Status);

        var cartTtl = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        var userTtl = await _fixture.Database.KeyTimeToLiveAsync(userKey);
        Assert.NotNull(cartTtl);
        Assert.NotNull(userTtl);
        Assert.True(cartTtl.Value <= TimeSpan.FromSeconds(30));
        Assert.True(userTtl.Value <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Save_AgainstExpiredCart_ReturnsCartMissing_AndCanBeRecreated()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        var missing = await store.SaveAsync(cartV1 with { Version = 9 }, expectedVersion: 9);
        Assert.Equal(CartSaveStatus.CartMissing, missing.Status);

        var recreate = await store.SaveAsync(cartV1 with { Version = 2 }, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, recreate.Status);

        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
    }
}

