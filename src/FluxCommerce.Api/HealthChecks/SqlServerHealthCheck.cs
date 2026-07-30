using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class SqlServerHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(HealthCheckResult.Degraded("SQL Server connection is not yet configured."));
    }
}
