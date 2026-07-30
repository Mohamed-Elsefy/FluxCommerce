using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class RabbitMQHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(HealthCheckResult.Degraded("RabbitMQ connection is not yet configured."));
    }
}
