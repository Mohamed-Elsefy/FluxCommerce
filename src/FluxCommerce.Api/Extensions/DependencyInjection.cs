using FluxCommerce.Api.Options;

namespace FluxCommerce.Api.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsConfiguration(configuration)
                .AddPresentationLayer()
                .AddInfrastructureLayer(configuration);

        return services;
    }

    private static IServiceCollection AddOptionsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<MessageBrokerOptions>(configuration.GetSection(MessageBrokerOptions.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        return services;
    }

    private static IServiceCollection AddPresentationLayer(this IServiceCollection services)
    {
        services.AddControllers();

        services.AddHealthChecks();

        return services;
    }

    private static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
