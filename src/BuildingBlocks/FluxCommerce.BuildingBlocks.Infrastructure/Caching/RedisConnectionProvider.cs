using StackExchange.Redis;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Caching;

public sealed class RedisConnectionProvider : IRedisConnectionProvider
{
    private readonly Lazy<IConnectionMultiplexer> _connection;

    public RedisConnectionProvider(string connectionString)
    {
        _connection = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(connectionString));
    }

    public IConnectionMultiplexer GetConnection() => _connection.Value;

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
