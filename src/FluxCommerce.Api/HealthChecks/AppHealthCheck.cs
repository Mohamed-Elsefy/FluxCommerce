using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class AppHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Application is running."));
    }
}
