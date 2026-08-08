using System.Text.RegularExpressions;
using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class RedisCartStoreTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    private static (RedisCartStore Store, RedisBacking Backing) CreateStore(
        string keyNamespace = "cart",
        int defaultTtlMinutes = 7 * 24 * 60)
    {
        var backing = new RedisBacking();

        var database = new Mock<IDatabase>();
        database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns<RedisKey, CommandFlags>((key, _) =>
                Task.FromResult(backing.Values.TryGetValue(key.ToString(), out var value) ? value : RedisValue.Null));
        database
            .Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns<string, RedisKey[], RedisValue[], CommandFlags>((_, keys, values, _) =>
                Task.FromResult(EvaluateCas(backing, keys, values)));
        database
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Returns<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>((key, value, expiry, _, _) =>
            {
                lock (backing.Values)
                {
                    backing.Values[key.ToString()] = value;
                    if (expiry is not null)
                        backing.Expiries[key.ToString()] = expiry.Value;
                }

                return Task.FromResult(true);
            });
        database
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .Returns<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((key, value, _, _, _) =>
            {
                lock (backing.Values)
                {
                    backing.Values[key.ToString()] = value;
                }

                return Task.FromResult(true);
            });
        database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns<RedisKey, CommandFlags>((key, _) =>
            {
                lock (backing.Values)
                {
                    backing.Expiries.Remove(key.ToString());
                    return Task.FromResult(backing.Values.Remove(key.ToString()));
                }
            });
        database
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns<RedisKey, CommandFlags>((key, _) => Task.FromResult(backing.Values.ContainsKey(key.ToString())));

        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        var connectionProvider = new Mock<IRedisConnectionProvider>();
        connectionProvider
            .Setup(p => p.GetConnection())
            .Returns(multiplexer.Object);

        var options = Options.Create(new CartOptions { KeyNamespace = keyNamespace, DefaultTtlMinutes = defaultTtlMinutes });

        var store = new RedisCartStore(
            connectionProvider.Object,
            new CartKeyGenerator(options),
            new CartStateSerializer(),
            new CartStateMapper(),
            options);

        return (store, backing);
    }

    private static RedisResult EvaluateCas(RedisBacking backing, RedisKey[] keys, RedisValue[] values)
    {
        lock (backing.Values)
        {
            var key = keys[0].ToString();
            var expected = (long)values[1];
            var ttlSeconds = (long)values[2];
            var current = backing.Values.TryGetValue(key, out var existing) ? existing.ToString() : null;

            if (current is null)
            {
                if (expected != 0)
                    return RedisResult.Create(CartCasScript.CartMissing);

                Write(backing, key, values[0], ttlSeconds);
                return RedisResult.Create(CartCasScript.Success);
            }

            var schemaMatch = Regex.Match(current, "\"schemaVersion\":(\\d+)");
            if (!schemaMatch.Success || long.Parse(schemaMatch.Groups[1].Value) != CartStateSerializer.SchemaVersion)
                return RedisResult.Create(CartCasScript.CorruptState);

            var versionMatch = Regex.Match(current, "\"version\":(\\d+)");
            if (!versionMatch.Success)
                return RedisResult.Create(CartCasScript.CorruptState);

            if (long.Parse(versionMatch.Groups[1].Value) != expected)
                return RedisResult.Create(CartCasScript.VersionConflict);

            Write(backing, key, values[0], ttlSeconds);
            return RedisResult.Create(CartCasScript.Success);
        }
    }

    private static void Write(RedisBacking backing, string key, RedisValue value, long ttlSeconds)
    {
        backing.Values[key] = value;

        if (ttlSeconds > 0)
        {
            backing.Expiries[key] = TimeSpan.FromSeconds(ttlSeconds);
            backing.ExpireCalls++;
        }
    }

    private sealed class RedisBacking
    {
        public Dictionary<string, RedisValue> Values { get; } = new();

        public Dictionary<string, TimeSpan> Expiries { get; } = new();

        public int ExpireCalls { get; set; }
    }

    // ---- Save ----

    [Fact]
    public async Task Save_ThenGet_ReturnsEquivalentState()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState(userId: Guid.NewGuid(), version: 7);

        var save = await store.SaveAsync(cart, expectedVersion: 0);

        Assert.Equal(CartSaveStatus.Success, save.Status);
        var loaded = await store.GetAsync(cart.CartId);
        AssertCartStateEqual(cart, loaded);
    }

    [Fact]
    public async Task Save_PersistsSerializedState_UnderGeneratedKey()
    {
        var (store, backing) = CreateStore();
        var cart = CreateCartState();

        await store.SaveAsync(cart, expectedVersion: 0);

        var expectedKey = $"cart:{cart.CartId.Value:D}";
        Assert.True(backing.Values.ContainsKey(expectedKey));
        Assert.Contains("schemaVersion", backing.Values[expectedKey].ToString());
    }

    [Fact]
    public async Task Save_AuthenticatedCart_WritesUserIndexKey()
    {
        var (store, backing) = CreateStore();
        var userId = Guid.NewGuid();
        var cart = CreateCartState(userId: userId);

        await store.SaveAsync(cart, expectedVersion: 0);

        var expectedUserKey = $"cart:user:{userId:D}";
        Assert.True(backing.Values.ContainsKey(expectedUserKey));
        Assert.Equal(cart.CartId.Value.ToString("D"), backing.Values[expectedUserKey].ToString());
    }

    [Fact]
    public async Task Save_GuestCart_DoesNotWriteUserIndexKey()
    {
        var (store, backing) = CreateStore();
        var cart = CreateCartState(userId: null);

        await store.SaveAsync(cart, expectedVersion: 0);

        Assert.DoesNotContain(backing.Values.Keys, key => key.StartsWith("cart:user:", StringComparison.Ordinal));
    }

    // ---- Get ----

    [Fact]
    public async Task Get_MissingCart_ReturnsNull()
    {
        var (store, _) = CreateStore();

        var loaded = await store.GetAsync(new CartId(Guid.NewGuid()));

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByUserId_AfterSave_ReturnsCart()
    {
        var (store, _) = CreateStore();
        var userId = Guid.NewGuid();
        var cart = CreateCartState(userId: userId);

        await store.SaveAsync(cart, expectedVersion: 0);

        var loaded = await store.GetByUserIdAsync(userId);
        AssertCartStateEqual(cart, loaded);
    }

    [Fact]
    public async Task GetByUserId_MissingUserKey_ReturnsNull()
    {
        var (store, _) = CreateStore();

        var loaded = await store.GetByUserIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByUserId_InvalidStoredUserValue_ReturnsNull()
    {
        var (store, backing) = CreateStore();
        var userId = Guid.NewGuid();
        backing.Values[$"cart:user:{userId:D}"] = "not-a-guid";

        var loaded = await store.GetByUserIdAsync(userId);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByUserId_ForDifferentUser_ReturnsNull()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState(userId: Guid.NewGuid());

        await store.SaveAsync(cart, expectedVersion: 0);

        var loaded = await store.GetByUserIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_ExistingCart_ReturnsTrue_AndRemovesCart()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState();

        await store.SaveAsync(cart, expectedVersion: 0);
        var deleted = await store.DeleteAsync(cart.CartId);

        Assert.True(deleted);
        Assert.False(await store.ExistsAsync(cart.CartId));
        Assert.Null(await store.GetAsync(cart.CartId));
    }

    [Fact]
    public async Task Delete_MissingCart_ReturnsFalse()
    {
        var (store, _) = CreateStore();

        var deleted = await store.DeleteAsync(new CartId(Guid.NewGuid()));

        Assert.False(deleted);
    }

    // ---- Exists ----

    [Fact]
    public async Task Exists_ExistingCart_ReturnsTrue()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState();

        await store.SaveAsync(cart, expectedVersion: 0);

        Assert.True(await store.ExistsAsync(cart.CartId));
    }

    [Fact]
    public async Task Exists_MissingCart_ReturnsFalse()
    {
        var (store, _) = CreateStore();

        Assert.False(await store.ExistsAsync(new CartId(Guid.NewGuid())));
    }

    // ---- Malformed stored state ----

    [Fact]
    public async Task Get_MalformedStoredState_Throws()
    {
        var (store, backing) = CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        backing.Values[$"cart:{cartId.Value:D}"] = """{"schemaVersion":1,"data":{"cartId":""";

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(cartId));
    }

    [Fact]
    public async Task Get_UnsupportedSchemaVersion_Throws()
    {
        var (store, backing) = CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        backing.Values[$"cart:{cartId.Value:D}"] = """{"schemaVersion":2,"data":{}}""";

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(cartId));
    }

    // ---- Isolation ----

    [Fact]
    public async Task Save_CartA_DoesNotMakeCartB_Exist()
    {
        var (store, _) = CreateStore();
        var cartA = CreateCartState();
        var cartB = CreateCartState();

        await store.SaveAsync(cartA, expectedVersion: 0);

        Assert.True(await store.ExistsAsync(cartA.CartId));
        Assert.False(await store.ExistsAsync(cartB.CartId));
        Assert.Null(await store.GetAsync(cartB.CartId));
    }

    [Fact]
    public async Task Get_CartA_NeverReturnsCartB()
    {
        var (store, _) = CreateStore();
        var cartA = CreateCartState();
        var cartB = CreateCartState();

        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);

        var loadedA = await store.GetAsync(cartA.CartId);
        var loadedB = await store.GetAsync(cartB.CartId);

        Assert.Equal(cartA.CartId, loadedA!.CartId);
        Assert.Equal(cartB.CartId, loadedB!.CartId);
        Assert.NotEqual(cartA.CartId, loadedB.CartId);
    }

    [Fact]
    public async Task Delete_CartA_DoesNotDeleteCartB()
    {
        var (store, _) = CreateStore();
        var cartA = CreateCartState();
        var cartB = CreateCartState();

        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);
        await store.DeleteAsync(cartA.CartId);

        Assert.False(await store.ExistsAsync(cartA.CartId));
        Assert.True(await store.ExistsAsync(cartB.CartId));
        Assert.NotNull(await store.GetAsync(cartB.CartId));
    }

    [Fact]
    public async Task Authenticated_UserA_IsIsolated_From_UserB()
    {
        var (store, _) = CreateStore();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var cartA = CreateCartState(userId: userA);

        await store.SaveAsync(cartA, expectedVersion: 0);

        var loadedA = await store.GetByUserIdAsync(userA);
        var loadedB = await store.GetByUserIdAsync(userB);

        AssertCartStateEqual(cartA, loadedA);
        Assert.Null(loadedB);
    }

    [Fact]
    public async Task Guest_And_Authenticated_Carts_AreIndependent()
    {
        var (store, _) = CreateStore();
        var guestCart = CreateCartState(userId: null);
        var userCart = CreateCartState(userId: Guid.NewGuid());

        await store.SaveAsync(guestCart, expectedVersion: 0);
        await store.SaveAsync(userCart, expectedVersion: 0);

        Assert.True(await store.ExistsAsync(guestCart.CartId));
        Assert.True(await store.ExistsAsync(userCart.CartId));
        AssertCartStateEqual(guestCart, await store.GetAsync(guestCart.CartId));
        AssertCartStateEqual(userCart, await store.GetAsync(userCart.CartId));
    }

    [Fact]
    public async Task DifferentNamespaces_AreIsolated()
    {
        var (storeA, _) = CreateStore("cart");
        var (storeB, _) = CreateStore("storefront-cart");
        var cart = CreateCartState();

        await storeA.SaveAsync(cart, expectedVersion: 0);

        Assert.True(await storeA.ExistsAsync(cart.CartId));
        Assert.False(await storeB.ExistsAsync(cart.CartId));
        Assert.Null(await storeB.GetAsync(cart.CartId));
    }

    // ---- Round-trip preservation ----

    [Fact]
    public async Task Save_ThenGet_PreservesVersionOwnershipAndItemSnapshots()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState(userId: Guid.NewGuid(), version: 12);

        await store.SaveAsync(cart, expectedVersion: 0);
        var loaded = await store.GetAsync(cart.CartId);

        Assert.NotNull(loaded);
        Assert.Equal(12, loaded!.Version);
        Assert.Equal(cart.UserId, loaded.UserId);
        Assert.Equal(cart.Items.Count, loaded.Items.Count);
        Assert.Equal(cart.Items[0].VariantId, loaded.Items[0].VariantId);
        Assert.Equal(cart.Items[0].ProductId, loaded.Items[0].ProductId);
        Assert.Equal(cart.Items[0].Sku, loaded.Items[0].Sku);
        Assert.Equal(cart.Items[0].ProductName, loaded.Items[0].ProductName);
        Assert.Equal(cart.Items[0].UnitPrice, loaded.Items[0].UnitPrice);
        Assert.Equal(cart.Items[0].Quantity, loaded.Items[0].Quantity);
    }

    // ---- TTL ----

    [Fact]
    public async Task Save_AppliesConfiguredTtl_ToCartKey()
    {
        var (store, backing) = CreateStore(defaultTtlMinutes: 30);
        var cart = CreateCartState();

        await store.SaveAsync(cart, expectedVersion: 0);

        var cartKey = $"cart:{cart.CartId.Value:D}";
        Assert.Equal(TimeSpan.FromMinutes(30), backing.Expiries[cartKey]);
        Assert.Equal(1, backing.ExpireCalls);
    }

    [Fact]
    public async Task Save_AuthenticatedCart_AppliesTtl_ToUserIndexKey()
    {
        var (store, backing) = CreateStore(defaultTtlMinutes: 45);
        var userId = Guid.NewGuid();
        var cart = CreateCartState(userId: userId);

        await store.SaveAsync(cart, expectedVersion: 0);

        var userKey = $"cart:user:{userId:D}";
        Assert.Equal(TimeSpan.FromMinutes(45), backing.Expiries[userKey]);
    }

    [Fact]
    public async Task SuccessfulMutation_RefreshesTtl()
    {
        var (store, backing) = CreateStore(defaultTtlMinutes: 30);
        var cart = CreateCartState(version: 1);

        await store.SaveAsync(cart, expectedVersion: 0);
        var beforeRefresh = backing.ExpireCalls;

        var mutated = cart with { Version = 2 };
        var save = await store.SaveAsync(mutated, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.Success, save.Status);
        Assert.Equal(beforeRefresh + 1, backing.ExpireCalls);
    }

    [Fact]
    public async Task Get_DoesNotRefreshTtl()
    {
        var (store, backing) = CreateStore(defaultTtlMinutes: 30);
        var cart = CreateCartState();
        await store.SaveAsync(cart, expectedVersion: 0);
        var beforeGet = backing.ExpireCalls;
        var cartKey = $"cart:{cart.CartId.Value:D}";
        var ttlBeforeGet = backing.Expiries[cartKey];

        await store.GetAsync(cart.CartId);
        await store.GetAsync(cart.CartId);
        await store.GetByUserIdAsync(Guid.NewGuid());

        Assert.Equal(beforeGet, backing.ExpireCalls);
        Assert.Equal(ttlBeforeGet, backing.Expiries[cartKey]);
    }

    [Fact]
    public async Task Exists_DoesNotRefreshTtl()
    {
        var (store, backing) = CreateStore(defaultTtlMinutes: 30);
        var cart = CreateCartState();
        await store.SaveAsync(cart, expectedVersion: 0);
        var beforeExists = backing.ExpireCalls;

        await store.ExistsAsync(cart.CartId);

        Assert.Equal(beforeExists, backing.ExpireCalls);
    }

    [Fact]
    public void GetTtl_ReturnsConfiguredTtl()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = 90 };

        var ttl = RedisCartStore.GetTtl(options);

        Assert.Equal(TimeSpan.FromMinutes(90), ttl);
    }

    [Fact]
    public void GetTtl_ReturnsDefaultTtl()
    {
        var ttl = RedisCartStore.GetTtl(new CartOptions());

        Assert.Equal(TimeSpan.FromMinutes(7 * 24 * 60), ttl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetTtl_NonPositiveTtl_Throws(int defaultTtlMinutes)
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = defaultTtlMinutes };

        Assert.Throws<ArgumentException>(() => RedisCartStore.GetTtl(options));
    }

    // ---- Versioning ----

    [Fact]
    public async Task MatchingVersion_Succeeds()
    {
        var (store, _) = CreateStore();
        var cartV1 = CreateCartState(version: 1);
        await store.SaveAsync(cartV1, expectedVersion: 0);

        var cartV2 = cartV1 with { Version = 2 };
        var save = await store.SaveAsync(cartV2, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.Success, save.Status);
        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
    }

    [Fact]
    public async Task StaleVersion_ReturnsVersionConflict()
    {
        var (store, _) = CreateStore();
        var cartV1 = CreateCartState(version: 1);
        await store.SaveAsync(cartV1, expectedVersion: 0);

        var cartV2 = cartV1 with { Version = 2 };
        await store.SaveAsync(cartV2, expectedVersion: 1);

        var stale = cartV1 with { Version = 2 };
        var save = await store.SaveAsync(stale, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.VersionConflict, save.Status);
        Assert.Null(save.Cart);
    }

    [Fact]
    public async Task StaleVersion_DoesNotOverwriteOrIncrementVersion()
    {
        var (store, backing) = CreateStore();
        var cartV1 = CreateCartState(version: 1, quantity: 2);
        await store.SaveAsync(cartV1, expectedVersion: 0);

        var cartV2 = cartV1 with { Version = 2, Items = [cartV1.Items[0] with { Quantity = 5 }] };
        await store.SaveAsync(cartV2, expectedVersion: 1);

        var staleWrite = cartV1 with { Version = 2, Items = [cartV1.Items[0] with { Quantity = 99 }] };
        var save = await store.SaveAsync(staleWrite, expectedVersion: 1);

        Assert.Equal(CartSaveStatus.VersionConflict, save.Status);
        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
        Assert.Equal(5, loaded.Items[0].Quantity);
        Assert.Equal(2, backing.ExpireCalls);
    }

    [Fact]
    public async Task Version_IsPersisted_AndReconstructed()
    {
        var (store, backing) = CreateStore();
        var cart = CreateCartState(version: 9);
        await store.SaveAsync(cart, expectedVersion: 0);

        var loaded = await store.GetAsync(cart.CartId);

        Assert.Equal(9, loaded!.Version);
        Assert.Contains("\"version\":9", backing.Values[$"cart:{cart.CartId.Value:D}"].ToString());
    }

    [Fact]
    public async Task Version_IsNotPartOfRedisKey()
    {
        var (store, backing) = CreateStore();
        var cart = CreateCartState(version: 5);
        await store.SaveAsync(cart, expectedVersion: 0);

        var cartKey = $"cart:{cart.CartId.Value:D}";
        Assert.True(backing.Values.ContainsKey(cartKey));
        Assert.False(backing.Values.ContainsKey($"cart:{cart.CartId.Value:D}:v5"));
        Assert.False(backing.Values.ContainsKey($"cart:{cart.CartId.Value:D}:version=5"));
    }

    // ---- Missing & create semantics ----

    [Fact]
    public async Task Save_ExpectedZero_WhenCartExists_ReturnsVersionConflict()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState(version: 1);
        await store.SaveAsync(cart, expectedVersion: 0);

        var another = cart with { Version = 2 };
        var save = await store.SaveAsync(another, expectedVersion: 0);

        Assert.Equal(CartSaveStatus.VersionConflict, save.Status);
    }

    [Fact]
    public async Task Save_ExpectedGreaterThanZero_WhenCartMissing_ReturnsCartMissing()
    {
        var (store, _) = CreateStore();
        var cart = CreateCartState(version: 5);

        var save = await store.SaveAsync(cart, expectedVersion: 5);

        Assert.Equal(CartSaveStatus.CartMissing, save.Status);
        Assert.Null(save.Cart);
        Assert.False(await store.ExistsAsync(cart.CartId));
    }

    // ---- Corrupt stored state on save ----

    [Fact]
    public async Task Save_StoredStateWithoutVersion_ThrowsInvalidDataException()
    {
        var (store, backing) = CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        backing.Values[$"cart:{cartId.Value:D}"] = """{"schemaVersion":1,"data":{"cartId":"11111111-1111-1111-1111-111111111111","items":[]}""";

        var cart = CreateCartState() with { CartId = cartId, Version = 2 };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(cart, expectedVersion: 1));
    }

    [Fact]
    public async Task Save_StoredStateWithUnsupportedSchema_ThrowsInvalidDataException()
    {
        var (store, backing) = CreateStore();
        var cartId = new CartId(Guid.NewGuid());
        backing.Values[$"cart:{cartId.Value:D}"] = """{"schemaVersion":2,"data":{}}""";

        var cart = CreateCartState() with { CartId = cartId, Version = 2 };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(cart, expectedVersion: 1));
    }

    // ---- Concurrency ----

    [Fact]
    public async Task ConcurrentSaves_WithSameExpectedVersion_OnlyOneSucceeds()
    {
        var (store, backing) = CreateStore();
        var cartV1 = CreateCartState(version: 1, quantity: 1);
        await store.SaveAsync(cartV1, expectedVersion: 0);
        var beforeBatch = backing.ExpireCalls;

        const int writers = 8;
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

        Assert.Equal(1, results.Count(r => r.Status == CartSaveStatus.Success));
        Assert.Equal(writers - 1, results.Count(r => r.Status == CartSaveStatus.VersionConflict));
        Assert.Equal(beforeBatch + 1, backing.ExpireCalls);

        var loaded = await store.GetAsync(cartV1.CartId);
        Assert.Equal(2, loaded!.Version);
        Assert.Contains(loaded.Items[0].Quantity, quantities);
    }

    [Fact]
    public async Task ConcurrentSaves_ForDifferentCarts_DoNotInterfere()
    {
        var (store, _) = CreateStore();
        var cartA = CreateCartState(version: 1);
        var cartB = CreateCartState(version: 1);
        await store.SaveAsync(cartA, expectedVersion: 0);
        await store.SaveAsync(cartB, expectedVersion: 0);

        var a2 = cartA with { Version = 2 };
        var b2 = cartB with { Version = 2 };

        var results = await Task.WhenAll(
            store.SaveAsync(a2, expectedVersion: 1),
            store.SaveAsync(b2, expectedVersion: 1));

        Assert.All(results, r => Assert.Equal(CartSaveStatus.Success, r.Status));
        Assert.Equal(2, (await store.GetAsync(cartA.CartId))!.Version);
        Assert.Equal(2, (await store.GetAsync(cartB.CartId))!.Version);
    }

    private static void AssertCartStateEqual(CartState expected, CartState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.CartId, actual!.CartId);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.Items, actual.Items);
    }

    private static CartState CreateCartState(Guid? userId = null, long version = 3, int quantity = 2) =>
        new(
            CartId: new CartId(Guid.NewGuid()),
            UserId: userId,
            Version: version,
            CreatedAtUtc: FixedTime,
            UpdatedAtUtc: FixedTime.AddMinutes(5),
            Items:
            [
                new CartItemState(
                    ProductId: new ProductId(Guid.NewGuid()),
                    VariantId: new VariantId(Guid.NewGuid()),
                    Sku: new Sku("SKU-1001"),
                    ProductName: "Wireless Mouse",
                    UnitPrice: new Money(24.99m, "USD"),
                    Quantity: quantity),
            ]);
}
