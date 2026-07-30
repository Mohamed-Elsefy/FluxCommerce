using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class RedisHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(HealthCheckResult.Degraded("Redis connection is not yet configured."));
    }
}
