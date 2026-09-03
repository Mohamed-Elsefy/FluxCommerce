using StackExchange.Redis;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Caching;

public sealed class RedisConnectionProvider : IRedisConnectionProvider
{
    private readonly Lazy<IConnectionMultiplexer> _connection;

    public RedisConnectionProvider(string connectionString)
    {
        _connection = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(CreateConfigurationOptions(connectionString)));
    }

    public IConnectionMultiplexer GetConnection() => _connection.Value;

    internal static ConfigurationOptions CreateConfigurationOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        return options;
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
