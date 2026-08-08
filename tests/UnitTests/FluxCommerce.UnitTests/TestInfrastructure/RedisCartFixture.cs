using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FluxCommerce.UnitTests.TestInfrastructure;

/// <summary>
/// Shared, fail-fast Redis fixture for the Cart integration test collection.
///
/// Lifecycle (per Phase 3.3.3):
///   1. Initialize — resolve the connection string (FLUXCOMMERCE_REDIS_CONNECTION, default localhost:6379).
///   2. Connect    — create the project's RedisConnectionProvider and connect to Redis.
///   3. Verify     — PING the server. Redis tests FAIL FAST (throw) when Redis is unreachable so that
///                   the suite never silently passes without real Redis.
///   4. Isolate    — allocate a unique per-run key namespace ("cart-it-{guid}:"). Every test in the
///                   collection writes under this namespace only.
///   5. Run tests  — tests exercise RedisCartStore against real Redis.
///   6. Cleanup    — delete only the keys created by the suite (deterministic cleanup; no KEYS/SCAN/flush).
/// </summary>
public sealed class RedisCartFixture : IDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_REDIS_CONNECTION";

    private const string DefaultConnection = "localhost:6379";

    private readonly IRedisConnectionProvider _provider;
    private readonly IDatabase _database;
    private readonly List<string> _trackedKeys = [];
    private readonly object _trackingLock = new();
    private readonly string _keyNamespace;

    public RedisCartFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) ?? DefaultConnection;

        _provider = new RedisConnectionProvider($"{connectionString},connectTimeout=2000,syncTimeout=5000");
        _database = _provider.GetConnection().GetDatabase();

        try
        {
            _database.Ping();
        }
        catch (Exception exception)
        {
            _provider.Dispose();
            throw new InvalidOperationException(
                "Cart Redis integration tests require a reachable Redis server and fail fast when it is unavailable. " +
                $"Start it with 'docker compose up -d redis' or override the connection through the " +
                $"'{ConnectionEnvironmentVariable}' environment variable. Underlying error: {exception.Message}",
                exception);
        }

        _keyNamespace = $"cart-it-{Guid.NewGuid():N}";
    }

    public string KeyNamespace => _keyNamespace;

    public IDatabase Database => _database;

    public IRedisConnectionProvider Provider => _provider;

    public string CartKey(FluxCommerce.Modules.Cart.Domain.ValueObjects.CartId cartId) =>
        $"{_keyNamespace}:{cartId.Value:D}";

    public string UserKey(Guid userId) => $"{_keyNamespace}:user:{userId:D}";

    /// <summary>
    /// Registers a key for deterministic cleanup at the end of the test run.
    /// </summary>
    public void Track(string key)
    {
        lock (_trackingLock)
        {
            _trackedKeys.Add(key);
        }
    }

    /// <summary>
    /// Registers every key a Save would touch for a cart state: the cart key and, for an
    /// authenticated cart, the user index key. This prevents test keys from leaking when a
    /// test asserts against an authenticated cart.
    /// </summary>
    public void Track(CartState cart)
    {
        Track(CartKey(cart.CartId));

        if (cart.UserId is Guid userId)
        {
            Track(UserKey(userId));
        }
    }

    public RedisCartStore CreateStore(int defaultTtlMinutes = 60) =>
        CreateStore(_keyNamespace, defaultTtlMinutes);

    public RedisCartStore CreateStore(string keyNamespace, int defaultTtlMinutes = 60)
    {
        var options = Options.Create(new CartOptions
        {
            KeyNamespace = keyNamespace,
            DefaultTtlMinutes = defaultTtlMinutes,
        });

        return new RedisCartStore(_provider, new CartKeyGenerator(options), new CartStateSerializer(), new CartStateMapper(), options);
    }

    public void Dispose()
    {
        DeleteTrackedKeys();
        DeleteNamespaceKeys();
        _provider.Dispose();
    }

    private void DeleteTrackedKeys()
    {
        lock (_trackingLock)
        {
            foreach (var key in _trackedKeys)
            {
                try
                {
                    _database.KeyDeleteAsync(key).GetAwaiter().GetResult();
                }
                catch
                {
                    // Best-effort cleanup: a leftover key inside the unique test namespace is harmless.
                }
            }

            _trackedKeys.Clear();
        }
    }

    private void DeleteNamespaceKeys()
    {
        // Safety net: remove any key created under this run's exclusive namespace that was not
        // explicitly tracked. SCAN (never KEYS) is scoped with MATCH to the unique per-run
        // namespace, so it touches only keys owned by this test run — never development data,
        // another test run, or another module.
        try
        {
            var connection = _provider.GetConnection();

            foreach (var endpoint in connection.GetEndPoints())
            {
                var server = connection.GetServer(endpoint);

                foreach (var key in server.Keys(database: _database.Database, pattern: $"{_keyNamespace}:*"))
                {
                    try
                    {
                        _database.KeyDeleteAsync(key).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
