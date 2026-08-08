using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies the real Redis key strategy and isolation: guest carts, authenticated carts,
/// guest-vs-authenticated addressing, namespace isolation and version-independent keys.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartKeyIsolationIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartKeyIsolationIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GuestCartA_And_GuestCartB_NeverOverlap()
    {
        var store = _fixture.CreateStore();
        var cartA = RedisCartTestData.Create(userId: null, quantity: 1);
        var cartB = RedisCartTestData.Create(userId: null, quantity: 2);
        _fixture.Track(_fixture.CartKey(cartA.CartId));
        _fixture.Track(_fixture.CartKey(cartB.CartId));

        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);

        Assert.NotEqual(_fixture.CartKey(cartA.CartId), _fixture.CartKey(cartB.CartId));
        RedisCartTestData.AssertCartStateEqual(cartA, await store.GetAsync(cartA.CartId));
        RedisCartTestData.AssertCartStateEqual(cartB, await store.GetAsync(cartB.CartId));
        Assert.Equal(1, (await store.GetAsync(cartA.CartId))!.Items[0].Quantity);
        Assert.Equal(2, (await store.GetAsync(cartB.CartId))!.Items[0].Quantity);
    }

    [Fact]
    public async Task UserA_And_UserB_Carts_AreCompletelyIndependent()
    {
        var store = _fixture.CreateStore();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var cartA = RedisCartTestData.Create(userId: userA, quantity: 3);
        var cartB = RedisCartTestData.Create(userId: userB, quantity: 4);
        _fixture.Track(_fixture.CartKey(cartA.CartId));
        _fixture.Track(_fixture.UserKey(userA));
        _fixture.Track(_fixture.CartKey(cartB.CartId));
        _fixture.Track(_fixture.UserKey(userB));

        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);

        var loadedA = await store.GetByUserIdAsync(userA);
        var loadedB = await store.GetByUserIdAsync(userB);

        RedisCartTestData.AssertCartStateEqual(cartA, loadedA);
        RedisCartTestData.AssertCartStateEqual(cartB, loadedB);
        Assert.Null(await store.GetByUserIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GuestCart_CannotAddress_AnAuthenticatedCartKey()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var guestCart = RedisCartTestData.Create(userId: null);
        var userCart = RedisCartTestData.Create(userId: userId);
        _fixture.Track(_fixture.CartKey(guestCart.CartId));
        _fixture.Track(_fixture.UserKey(userId));
        _fixture.Track(_fixture.CartKey(userCart.CartId));

        await store.SaveAsync(guestCart, expectedVersion: 0);
        await store.SaveAsync(userCart, expectedVersion: 0);

        // The guest cart key is distinct from the user index key and from the user's cart key.
        Assert.NotEqual(_fixture.CartKey(guestCart.CartId), _fixture.UserKey(userId));

        // Using the guest cart's identifier as a user id must resolve to nothing: a guest
        // cart can never address the authenticated user's cart key through the user lookup.
        Assert.Null(await store.GetByUserIdAsync(guestCart.CartId.Value));

        // Each cart is only reachable through its own key, and the user index resolves the user cart.
        RedisCartTestData.AssertCartStateEqual(guestCart, await store.GetAsync(guestCart.CartId));
        RedisCartTestData.AssertCartStateEqual(userCart, await store.GetAsync(userCart.CartId));
        RedisCartTestData.AssertCartStateEqual(userCart, await store.GetByUserIdAsync(userId));
        Assert.NotEqual(guestCart.CartId, userCart.CartId);
    }

    [Fact]
    public async Task TwoConfiguredNamespaces_NeverAddressTheSameCart()
    {
        // Test-only namespaces: the test must never write into a production key namespace.
        const string namespaceA = "cart-iso-a";
        const string namespaceB = "cart-iso-b";
        var storePrimary = _fixture.CreateStore(namespaceA);
        var storeSecondary = _fixture.CreateStore(namespaceB);
        var cart = RedisCartTestData.Create();
        _fixture.Track($"{namespaceA}:{cart.CartId.Value:D}");
        _fixture.Track($"{namespaceB}:{cart.CartId.Value:D}");

        await storePrimary.SaveAsync(cart, expectedVersion: 0);

        Assert.True(await storePrimary.ExistsAsync(cart.CartId));
        Assert.False(await storeSecondary.ExistsAsync(cart.CartId));
        Assert.Null(await storeSecondary.GetAsync(cart.CartId));
    }

    [Fact]
    public async Task ChangingCartVersion_DoesNotChangeItsRedisKey()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        var cartV2 = cartV1 with { Version = 2 };
        var save = await store.SaveAsync(cartV2, expectedVersion: 1);
        Assert.Equal(CartSaveStatus.Success, save.Status);

        Assert.True(await _fixture.Database.KeyExistsAsync(cartKey));
        Assert.False(await _fixture.Database.KeyExistsAsync($"{cartKey}:v2"));
        Assert.False(await _fixture.Database.KeyExistsAsync($"{cartKey}:version=2"));

        var raw = (await _fixture.Database.StringGetAsync(cartKey)).ToString();
        Assert.Contains("\"version\":2", raw);
    }

    [Fact]
    public async Task UserIndexKey_AlwaysPointsToTheUsersCurrentCartKey()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId);
        _fixture.Track(_fixture.CartKey(cart.CartId));
        _fixture.Track(_fixture.UserKey(userId));

        await store.SaveAsync(cart, expectedVersion: 0);

        var userValue = (await _fixture.Database.StringGetAsync(_fixture.UserKey(userId))).ToString();
        Assert.Equal($"{_fixture.KeyNamespace}:{userValue}", _fixture.CartKey(cart.CartId));
    }
}
