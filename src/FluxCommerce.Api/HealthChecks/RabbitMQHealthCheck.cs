using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxCommerce.Api.HealthChecks;

public sealed class RabbitMQHealthCheck : IHealthCheck
{
    private readonly IRabbitMQConnectionProvider _connectionProvider;

    public RabbitMQHealthCheck(IRabbitMQConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var channel = await _connectionProvider.CreateChannelAsync(cancellationToken);
            return channel.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ is reachable.")
                : HealthCheckResult.Degraded("RabbitMQ channel is closed.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded("RabbitMQ is not reachable.", exception);
        }
    }
}
