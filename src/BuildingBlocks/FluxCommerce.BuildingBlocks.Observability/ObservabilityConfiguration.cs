using FluxCommerce.BuildingBlocks.Observability.Resources;
using FluxCommerce.BuildingBlocks.Observability.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace FluxCommerce.BuildingBlocks.Observability;

public static class ObservabilityConfiguration
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.ConfigurePlatformResource(configuration))
            .WithTracing(tracing => tracing
                .AddSource(ActivitySources.FluxCommerce)
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ActivitySources.FluxCommerce)
                .AddConsoleExporter());

        return services;
    }
}
