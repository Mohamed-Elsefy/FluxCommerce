using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;

namespace FluxCommerce.BuildingBlocks.Observability.Resources;

public static class ResourceConfiguration
{
    private const string ServiceNameKey = "Observability:ServiceName";

    private const string DefaultServiceName = "FluxCommerce.Api";

    public static ResourceBuilder ConfigurePlatformResource(this ResourceBuilder resource, IConfiguration configuration)
    {
        var serviceName = configuration[ServiceNameKey] ?? DefaultServiceName;
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";

        return resource
            .AddService(
                serviceName: serviceName,
                serviceVersion: typeof(ResourceConfiguration).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", environment)
            });
    }
}
