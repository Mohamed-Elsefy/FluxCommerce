using FluxCommerce.Api.HealthChecks;
using FluxCommerce.Api.Options;
using FluxCommerce.Api.Security;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Observability;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.BuildingBlocks.Web.Middleware;
using FluxCommerce.Modules.Cart.Infrastructure;
using FluxCommerce.Modules.Catalog.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Payments.Infrastructure;
using FluxCommerce.Modules.Identity.Infrastructure;

namespace FluxCommerce.Api.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsConfiguration(configuration)
                .AddPresentationLayer()
                .AddInfrastructureLayer(configuration)
                .AddObservability(configuration);

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
        services.AddIdentityInfrastructure(GetConnectionString(configuration, "Identity"));

        services.AddRedis(GetRedisConnectionString(configuration));
        services.AddRabbitMQ(GetRabbitMQConnectionString(configuration));

        services.AddHealthChecks()
                .AddCheck<AppHealthCheck>("app", tags: ["app"])
                .AddCheck<SqlServerHealthCheck>("sql_server", tags: ["database", "infrastructure"])
                .AddCheck<RedisHealthCheck>("redis", tags: ["cache", "infrastructure"])
                .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: ["messaging", "infrastructure"]);

        return services;
    }

    private static string GetRedisConnectionString(IConfiguration configuration)
    {
        return configuration.GetSection($"{CacheOptions.SectionName}:ConnectionString").Value
            ?? throw new InvalidOperationException("Redis connection string is not configured.");
    }

    private static string GetRabbitMQConnectionString(IConfiguration configuration)
    {
        var options = configuration.GetSection(MessageBrokerOptions.SectionName).Get<MessageBrokerOptions>()
            ?? throw new InvalidOperationException("RabbitMQ connection is not configured.");

        if (string.IsNullOrWhiteSpace(options.Host))
            throw new InvalidOperationException("RabbitMQ host is not configured.");

        var credentials = string.IsNullOrEmpty(options.Username)
            ? string.Empty
            : $"{Uri.EscapeDataString(options.Username)}:{Uri.EscapeDataString(options.Password ?? string.Empty)}@";

        var virtualHost = options.VirtualHost.TrimStart('/');
        var path = string.IsNullOrEmpty(virtualHost) ? string.Empty : Uri.EscapeDataString(virtualHost);

        return $"amqp://{credentials}{options.Host}:{options.Port}/{path}";
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
