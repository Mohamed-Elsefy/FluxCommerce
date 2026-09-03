using StackExchange.Redis;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Caching;

public interface IRedisConnectionProvider : IDisposable
{
    IConnectionMultiplexer GetConnection();
}
