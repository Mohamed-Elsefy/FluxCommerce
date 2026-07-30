using FluxCommerce.Api.HealthChecks;
using FluxCommerce.Api.Options;
using FluxCommerce.BuildingBlocks.Persistence;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.BuildingBlocks.Web.Middleware;
using FluxCommerce.Modules.Cart.Infrastructure;
using FluxCommerce.Modules.Catalog.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Payments.Infrastructure;

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
        services.AddExceptionHandler<ExceptionHandler>();
        services.AddProblemDetails();

        services.AddControllers();

        return services;
    }

    private static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser>(_ => new CurrentUser());
        services.AddScoped<DomainEventInterceptor>();

        services.AddPersistence(configuration);

        services.AddCatalogInfrastructure(GetConnectionString(configuration, "Catalog"));
        services.AddCartInfrastructure(GetConnectionString(configuration, "Cart"));
        services.AddOrdersInfrastructure(GetConnectionString(configuration, "Orders"));
        services.AddInventoryInfrastructure(GetConnectionString(configuration, "Inventory"));
        services.AddPaymentsInfrastructure(GetConnectionString(configuration, "Payments"));

        services.AddHealthChecks()
                .AddCheck<AppHealthCheck>("app", tags: ["app"])
                .AddCheck<SqlServerHealthCheck>("sql_server", tags: ["database", "infrastructure"])
                .AddCheck<RedisHealthCheck>("redis", tags: ["cache", "infrastructure"])
                .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: ["messaging", "infrastructure"]);

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration, string moduleName)
    {
        var moduleSection = configuration.GetSection($"{DatabaseOptions.SectionName}:ModuleConnections:{moduleName}");
        var moduleConnection = moduleSection.Value;

        if (!string.IsNullOrWhiteSpace(moduleConnection))
            return moduleConnection;

        return configuration.GetSection($"{DatabaseOptions.SectionName}:ConnectionString").Value
            ?? throw new InvalidOperationException($"Database connection string is not configured for module '{moduleName}'.");
    }
}
