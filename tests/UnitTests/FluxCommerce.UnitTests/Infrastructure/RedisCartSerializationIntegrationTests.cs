using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies Cart serialization against actual Redis storage: the round-trip, schema version,
/// camelCase representation, monetary/date-time conventions, and rejection of malformed or
/// unsupported stored state. No schema migration functionality is introduced.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class RedisCartSerializationIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public RedisCartSerializationIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StoredJson_UsesSchemaVersionEnvelope_AndCamelCase()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 5);
        _fixture.Track(cart);

        await store.SaveAsync(cart, expectedVersion: 0);

        var raw = (await _fixture.Database.StringGetAsync(_fixture.CartKey(cart.CartId))).ToString();

        Assert.Contains("\"schemaVersion\":1", raw);
        Assert.Contains("\"data\":", raw);
        Assert.Contains("\"cartId\"", raw);
        Assert.Contains("\"version\":5", raw);
        Assert.Contains("\"createdAtUtc\"", raw);
        Assert.Contains("\"updatedAtUtc\"", raw);
        Assert.Contains("\"items\"", raw);
        Assert.Contains("\"productId\"", raw);
        Assert.Contains("\"variantId\"", raw);
        Assert.Contains("\"sku\"", raw);
        Assert.Contains("\"productName\"", raw);
        Assert.Contains("\"unitPrice\"", raw);
        Assert.Contains("\"currency\"", raw);
        Assert.Contains("\"quantity\"", raw);
        Assert.DoesNotContain("\"CartId\"", raw);
    }

    [Fact]
    public async Task StoredJson_UsesIso8601Timestamps_AndPreservesMonetaryPrecision()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create(version: 1, unitPrice: 19.9900m);
        _fixture.Track(_fixture.CartKey(cart.CartId));

        await store.SaveAsync(cart, expectedVersion: 0);

        var raw = (await _fixture.Database.StringGetAsync(_fixture.CartKey(cart.CartId))).ToString();

        Assert.Contains("\"2026-08-07T10:30:00+00:00\"", raw);
        Assert.Contains("\"2026-08-07T10:35:00+00:00\"", raw);

        var loaded = await store.GetAsync(cart.CartId);
        Assert.Equal(19.9900m, loaded!.Items[0].UnitPrice.Amount);
    }

    [Fact]
    public async Task RoundTrip_ThroughRealRedis_PreservesAllState()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var cart = RedisCartTestData.Create(userId: userId, version: 9, quantity: 7, unitPrice: 45.10m);
        _fixture.Track(cart);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        RedisCartTestData.AssertCartStateEqual(cart, loaded);
        Assert.Equal(cart.Items[0].VariantId, loaded!.Items[0].VariantId);
        Assert.Equal(cart.Items[0].Sku, loaded.Items[0].Sku);
        Assert.Equal(cart.Items[0].UnitPrice.Currency, loaded.Items[0].UnitPrice.Currency);
    }

    [Fact]
    public async Task Get_StoredMalformedJson_ThrowsInvalidDataException()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        await _fixture.Database.StringSetAsync(cartKey, """{"schemaVersion":1,"data":{"cartId":""");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(cartId));
    }

    [Fact]
    public async Task Get_StoredUnsupportedSchemaVersion_ThrowsInvalidDataException()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        await _fixture.Database.StringSetAsync(cartKey, """{"schemaVersion":2,"data":{}}""");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(cartId));
    }

    [Fact]
    public async Task Save_StoredStateWithoutVersion_ThrowsInvalidDataException()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        await _fixture.Database.StringSetAsync(
            cartKey,
            """{"schemaVersion":1,"data":{"cartId":"11111111-1111-1111-1111-111111111111","items":[]}""");

        var cart = RedisCartTestData.Create() with { CartId = cartId, Version = 2 };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(cart, expectedVersion: 1));
    }

    [Fact]
    public async Task Save_StoredUnsupportedSchema_ThrowsInvalidDataException()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        await _fixture.Database.StringSetAsync(cartKey, """{"schemaVersion":2,"data":{}}""");

        var cart = RedisCartTestData.Create() with { CartId = cartId, Version = 2 };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(cart, expectedVersion: 1));
    }

    [Fact]
    public async Task Save_StoredMalformedJson_ThrowsInvalidDataException()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        await _fixture.Database.StringSetAsync(cartKey, """not-json""");

        var cart = RedisCartTestData.Create() with { CartId = cartId, Version = 2 };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(cart, expectedVersion: 1));
    }

    [Fact]
    public async Task InvalidStoredState_DoesNotSilentlyCreateInvalidCart()
    {
        var store = _fixture.CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        var cartKey = _fixture.CartKey(cartId);
        _fixture.Track(cartKey);

        // Valid envelope, but an empty cart id violates document validation.
        await _fixture.Database.StringSetAsync(
            cartKey,
            """{"schemaVersion":1,"data":{"cartId":"00000000-0000-0000-0000-000000000000","items":[]}""");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(cartId));
    }

    [Fact]
    public async Task Save_ThenGet_HandlesEmptyCartItems()
    {
        var store = _fixture.CreateStore();
        var cart = RedisCartTestData.Create() with { Items = [] };
        _fixture.Track(_fixture.CartKey(cart.CartId));

        var save = await store.SaveAsync(cart, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, save.Status);

        var loaded = await store.GetAsync(cart.CartId);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Items);
        Assert.Equal(cart.Version, loaded.Version);
    }
}

