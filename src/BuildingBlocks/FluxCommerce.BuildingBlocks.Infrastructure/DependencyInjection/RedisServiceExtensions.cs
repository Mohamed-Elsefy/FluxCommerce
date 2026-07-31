using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;

public static class RedisServiceExtensions
{
    public static IServiceCollection AddRedis(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IRedisConnectionProvider>(new RedisConnectionProvider(connectionString));

        return services;
    }
}
