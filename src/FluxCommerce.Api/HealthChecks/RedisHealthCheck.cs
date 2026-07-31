using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IRedisConnectionProvider _connectionProvider;

    public RedisHealthCheck(IRedisConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            var database = _connectionProvider.GetConnection().GetDatabase();
            await database.PingAsync();

            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded("Redis is not reachable.", exception);
        }
    }
}
