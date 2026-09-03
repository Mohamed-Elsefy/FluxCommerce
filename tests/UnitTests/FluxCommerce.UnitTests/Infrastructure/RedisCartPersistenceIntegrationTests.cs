using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies the Cart persistence contract (Save / Get / Delete / Exists / Missing Cart)
/// against a real Redis instance.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartPersistenceIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartPersistenceIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Save ----

    [Fact]
    public async Task Save_PersistsCompleteCartState_UnderGeneratedKey()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 4, quantity: 3);
        _fixture.Track(cart);

        var save = await store.SaveAsync(cart, expectedVersion: 0);

        Assert.Equal(CartSaveStatus.Success, save.Status);
        Assert.Same(cart, save.Cart);

        Assert.True(await _fixture.Database.KeyExistsAsync(_fixture.CartKey(cart.CartId)));
        var raw = await _fixture.Database.StringGetAsync(_fixture.CartKey(cart.CartId));
        Assert.True(raw.HasValue);
        Assert.Contains("\"schemaVersion\":1", raw.ToString());
        Assert.Contains($"\"cartId\":\"{cart.CartId.Value:D}\"", raw.ToString());
        Assert.Contains("\"version\":4", raw.ToString());
    }

    [Fact]
    public async Task Save_AuthenticatedCart_WritesUserIndexKey()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId);
        _fixture.Track(_fixture.CartKey(cart.CartId));
        _fixture.Track(_fixture.UserKey(userId));

        await store.SaveAsync(cart, expectedVersion: 0);

        var userValue = await _fixture.Database.StringGetAsync(_fixture.UserKey(userId));
        Assert.True(userValue.HasValue);
        Assert.Equal(cart.CartId.Value.ToString("D"), userValue.ToString());
    }

    [Fact]
    public async Task Save_GuestCart_DoesNotWriteAnyUserIndexKey()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(userId: null);
        _fixture.Track(_fixture.CartKey(cart.CartId));

        await store.SaveAsync(cart, expectedVersion: 0);

        Assert.True(await _fixture.Database.KeyExistsAsync(_fixture.CartKey(cart.CartId)));
    }

    // ---- Get ----

    [Fact]
    public async Task Get_AfterSave_ReturnsEquivalentCartState()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 7, quantity: 5, unitPrice: 19.99m);
        _fixture.Track(cart);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        RedisCartTestData.AssertCartStateEqual(cart, loaded);
    }

    [Fact]
    public async Task Get_PreservesOwnershipItemsQuantitySnapshotAndVersion()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId, version: 12, quantity: 4, unitPrice: 129.50m);
        _fixture.Track(cart);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        Assert.NotNull(loaded);
        Assert.Equal(cart.CartId, loaded!.CartId);
        Assert.Equal(userId, loaded.UserId);
        Assert.Equal(12, loaded.Version);
        Assert.Single(loaded.Items);
        Assert.Equal(cart.Items[0].ProductId, loaded.Items[0].ProductId);
        Assert.Equal(cart.Items[0].VariantId, loaded.Items[0].VariantId);
        Assert.Equal(cart.Items[0].Sku, loaded.Items[0].Sku);
        Assert.Equal(cart.Items[0].ProductName, loaded.Items[0].ProductName);
        Assert.Equal(cart.Items[0].UnitPrice, loaded.Items[0].UnitPrice);
        Assert.Equal(cart.Items[0].Quantity, loaded.Items[0].Quantity);
    }

    [Fact]
    public async Task GetByUserId_ReturnsTheUsersOwnCart()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId);
        _fixture.Track(_fixture.CartKey(cart.CartId));
        _fixture.Track(_fixture.UserKey(userId));

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetByUserIdAsync(userId);

        RedisCartTestData.AssertCartStateEqual(cart, loaded);
    }

    [Fact]
    public async Task GetByUserId_UnknownUser_ReturnsNull()
    {
        var store = _fixture.CreateStore();

        var loaded = await store.GetByUserIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_ExistingCart_ReturnsTrue_AndCartIsNoLongerRetrievable()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create();
        _fixture.Track(_fixture.CartKey(cart.CartId));

        await store.SaveAsync(cart, expectedVersion: 0);

        var deleted = await store.DeleteAsync(cart.CartId);

        Assert.True(deleted);
        Assert.False(await store.ExistsAsync(cart.CartId));
        Assert.Null(await store.GetAsync(cart.CartId));
    }

    [Fact]
    public async Task Delete_MissingCart_ReturnsFalse_AndRemainsSafe()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        _fixture.Track(_fixture.CartKey(cartId));

        var deleted = await store.DeleteAsync(cartId);

        Assert.False(deleted);
        Assert.Null(await store.GetAsync(cartId));
    }

    // ---- Exists ----

    [Fact]
    public async Task Exists_IsFalseBeforeSave_TrueAfterSave_FalseAfterDelete()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create();
        _fixture.Track(_fixture.CartKey(cart.CartId));

        Assert.False(await store.ExistsAsync(cart.CartId));

        await store.SaveAsync(cart, expectedVersion: 0);
        Assert.True(await store.ExistsAsync(cart.CartId));

        await store.DeleteAsync(cart.CartId);
        Assert.False(await store.ExistsAsync(cart.CartId));
    }

    // ---- Missing Cart ----

    [Fact]
    public async Task MissingCart_GetReturnsNull_ExistsFalse_DeleteSafe()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        _fixture.Track(_fixture.CartKey(cartId));

        Assert.Null(await store.GetAsync(cartId));
        Assert.False(await store.ExistsAsync(cartId));
        Assert.False(await store.DeleteAsync(cartId));
    }

    [Fact]
    public async Task MissingCart_SaveWithExpectedVersion_GreaterThanZero_ReturnsCartMissing()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(version: 3);
        _fixture.Track(_fixture.CartKey(cart.CartId));

        var save = await store.SaveAsync(cart, expectedVersion: 3);

        Assert.Equal(CartSaveStatus.CartMissing, save.Status);
        Assert.Null(save.Cart);
        Assert.False(await store.ExistsAsync(cart.CartId));
    }
}

