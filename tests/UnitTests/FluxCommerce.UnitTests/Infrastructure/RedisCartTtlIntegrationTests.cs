using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies Cart TTL behavior against real Redis expiration semantics. Timing tolerances are
/// used instead of exact millisecond values. The minimum store TTL is one minute, so short
/// expirations are induced directly on the Redis key to observe real expiration.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartTtlIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartTtlIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Save_AppliesConfiguredTtl_ToCartKey()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cart = RedisCartTestData.Create();
        var cartKey = _fixture.CartKey(cart.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cart, expectedVersion: 0);

        var ttl = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromSeconds(50) && ttl.Value <= TimeSpan.FromSeconds(60),
            $"Expected a 60 second TTL, got {ttl.Value}");
    }

    [Fact]
    public async Task Save_AuthenticatedCart_AppliesTtl_ToUserIndexKey()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId);
        var cartKey = _fixture.CartKey(cart.CartId);
        var userKey = _fixture.UserKey(userId);
        _fixture.Track(cartKey);
        _fixture.Track(userKey);

        await store.SaveAsync(cart, expectedVersion: 0);

        var ttl = await _fixture.Database.KeyTimeToLiveAsync(userKey);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromSeconds(50) && ttl.Value <= TimeSpan.FromSeconds(60),
            $"Expected a 60 second TTL on the user index key, got {ttl.Value}");
    }

    [Fact]
    public async Task SuccessfulMutation_RefreshesTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromSeconds(10));

        var cartV2 = cartV1 with { Version = 2 };
        var save = await store.SaveAsync(cartV2, expectedVersion: 1);
        Assert.Equal(CartSaveStatus.Success, save.Status);

        var ttlAfterMutation = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlAfterMutation);
        Assert.True(ttlAfterMutation.Value > TimeSpan.FromSeconds(50) && ttlAfterMutation.Value <= TimeSpan.FromSeconds(60),
            $"A successful mutation must refresh the TTL to ~60s, got {ttlAfterMutation.Value}");
    }

    [Fact]
    public async Task Get_DoesNotRefreshTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cart = RedisCartTestData.Create();
        var cartKey = _fixture.CartKey(cart.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cart, expectedVersion: 0);

        var ttlBefore = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        await Task.Delay(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 5; i++)
        {
            await store.GetAsync(cart.CartId);
        }

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlBefore);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter.Value <= ttlBefore.Value - TimeSpan.FromSeconds(1),
            $"Repeated Gets must not extend the Cart lifetime (before={ttlBefore.Value}, after={ttlAfter.Value})");
    }

    [Fact]
    public async Task Exists_DoesNotRefreshTtl()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cart = RedisCartTestData.Create();
        var cartKey = _fixture.CartKey(cart.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cart, expectedVersion: 0);

        var ttlBefore = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        await Task.Delay(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 5; i++)
        {
            Assert.True(await store.ExistsAsync(cart.CartId));
        }

        var ttlAfter = await _fixture.Database.KeyTimeToLiveAsync(cartKey);
        Assert.NotNull(ttlBefore);
        Assert.NotNull(ttlAfter);
        Assert.True(ttlAfter.Value <= ttlBefore.Value - TimeSpan.FromSeconds(1),
            $"Exists must not extend the Cart lifetime (before={ttlBefore.Value}, after={ttlAfter.Value})");
    }

    [Fact]
    public async Task Cart_Expires_AndBehavesAsMissing()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        Assert.False(await store.ExistsAsync(cartV1.CartId));
        Assert.Null(await store.GetAsync(cartV1.CartId));

        var save = await store.SaveAsync(cartV1 with { Version = 5 }, expectedVersion: 5);
        Assert.Equal(CartSaveStatus.CartMissing, save.Status);
    }

    [Fact]
    public async Task ExpiredCart_IsNotReturnedThroughUserLookup()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId, version: 1);
        var cartKey = _fixture.CartKey(cart.CartId);
        var userKey = _fixture.UserKey(userId);
        _fixture.Track(cartKey);
        _fixture.Track(userKey);

        await store.SaveAsync(cart, expectedVersion: 0);
        Assert.NotNull(await store.GetByUserIdAsync(userId));

        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        // The cart key has expired; the stale user-index pointer must not resurrect the cart.
        Assert.Null(await store.GetByUserIdAsync(userId));
    }
}

