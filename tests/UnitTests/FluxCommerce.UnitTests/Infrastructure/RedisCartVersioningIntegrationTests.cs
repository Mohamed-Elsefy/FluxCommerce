using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies Cart version persistence and increment behavior against real Redis: the initial
/// version, version survival across Save → Get, exactly-once increments, key/version
/// independence, and parity between the returned Cart and the persisted Redis state.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartVersioningIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartVersioningIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitialVersion_IsPersisted()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(version: Cart.InitialVersion);
        var cartKey = _fixture.CartKey(cart.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        Assert.Equal(Cart.InitialVersion, loaded!.Version);
        Assert.Contains("\"version\":1", (await _fixture.Database.StringGetAsync(cartKey)).ToString());
    }

    [Fact]
    public async Task Version_Survives_SaveThenGet()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(version: 15);
        var cartKey = _fixture.CartKey(cart.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        Assert.Equal(15, loaded!.Version);
        Assert.Contains("\"version\":15", (await _fixture.Database.StringGetAsync(cartKey)).ToString());
    }

    [Fact]
    public async Task DomainMutation_IncrementsExactlyOnce_AndMatchesPersistedState()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        var cart = Cart.CreateGuest(cartId, RedisCartTestData.FixedTime);
        Assert.Equal(Cart.InitialVersion, cart.Version);

        var state = ToState(cart);
        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(state, expectedVersion: 0)).Status);
        Assert.Equal(1, (await store.GetAsync(cartId))!.Version);

        // First mutation: exactly one version increment.
        var add = cart.AddItem(
            new ProductId(Guid.NewGuid()),
            new VariantId(Guid.NewGuid()),
            new Sku("SKU-2001"),
            "Mechanical Keyboard",
            new Money(89.00m, "USD"),
            quantity: 1,
            RedisCartTestData.FixedTime.AddMinutes(1));
        Assert.True(add.IsSuccess);
        Assert.Equal(2, cart.Version);

        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(ToState(cart), expectedVersion: 1)).Status);
        Assert.Equal(2, (await store.GetAsync(cartId))!.Version);
        Assert.Contains("\"version\":2", (await _fixture.Database.StringGetAsync(cartKey)).ToString());

        // Second mutation: exactly one more increment, and the persisted version stays in sync.
        var update = cart.UpdateQuantity(
            cart.Items.First().VariantId,
            quantity: 3,
            RedisCartTestData.FixedTime.AddMinutes(2));
        Assert.True(update.IsSuccess);
        Assert.Equal(3, cart.Version);

        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(ToState(cart), expectedVersion: 2)).Status);
        Assert.Equal(3, (await store.GetAsync(cartId))!.Version);
        Assert.Contains("\"version\":3", (await _fixture.Database.StringGetAsync(cartKey)).ToString());
    }

    [Fact]
    public async Task Version_IsNotPartOfTheRedisKey()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);
        var cartV2 = cartV1 with { Version = 2 };
        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(cartV2, expectedVersion: 1)).Status);

        Assert.True(await _fixture.Database.KeyExistsAsync(cartKey));
        Assert.False(await _fixture.Database.KeyExistsAsync($"{cartKey}:v2"));
        Assert.False(await _fixture.Database.KeyExistsAsync($"{cartKey}:2"));
    }

    [Fact]
    public async Task StaleSave_DoesNotChangeVersion_OrState()
    {
        var store = _fixture.CreateStore();
        var cartV1 = RedisCartTestData.Create(version: 1, quantity: 2);
        var cartKey = _fixture.CartKey(cartV1.CartId);
        _fixture.Track(cartKey);

        await store.SaveAsync(cartV1, expectedVersion: 0);

        var cartV2 = cartV1 with { Version = 2, Items = [cartV1.Items[0] with { Quantity = 5 }] };
        Assert.Equal(CartSaveStatus.Success, (await store.SaveAsync(cartV2, expectedVersion: 1)).Status);

        var stale = cartV1 with { Version = 2, Items = [cartV1.Items[0] with { Quantity = 99 }] };
        Assert.Equal(CartSaveStatus.VersionConflict, (await store.SaveAsync(stale, expectedVersion: 1)).Status);

        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
        Assert.Equal(5, loaded.Items[0].Quantity);
        Assert.Contains("\"version\":2", (await _fixture.Database.StringGetAsync(cartKey)).ToString());
        Assert.DoesNotContain("\"quantity\":99", (await _fixture.Database.StringGetAsync(cartKey)).ToString());
    }

    private static CartState ToState(Cart cart) =>
        new(
            cart.Id,
            cart.UserId,
            cart.Version,
            cart.CreatedAtUtc,
            cart.UpdatedAtUtc,
            cart.Items
                .Select(item => new CartItemState(
                    item.ProductId,
                    item.VariantId,
                    item.Sku,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity))
                .ToList());
}
