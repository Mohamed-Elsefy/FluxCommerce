using FluxCommerce.Api.HealthChecks;
using FluxCommerce.Api.Json;
using FluxCommerce.Api.Options;
using FluxCommerce.Api.Localization;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Localization.DependencyInjection;
using FluxCommerce.BuildingBlocks.Observability;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Middleware;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
                .AddSwaggerConfiguration()
                .AddLocalizationConfiguration()
                .AddLocalizationServices()
                .AddJwtAuthentication(configuration)
                .AddEmailOptions(configuration)
                .AddInfrastructureLayer(configuration)
                .AddObservability(configuration);

        return services;
    }

    private static IServiceCollection AddOptionsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MessageBrokerOptions>()
            .Bind(configuration.GetSection(MessageBrokerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddPresentationLayer(this IServiceCollection services)
    {
        services.AddExceptionHandler<ExceptionHandler>();
        services.AddProblemDetails();

        services.AddSingleton<IErrorCodeLocalizer, ErrorCodeLocalizer>();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new NullableGuidJsonConverter());
        });

        return services;
    }

    private static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddPersistence(configuration);

        services.AddCatalogInfrastructure(GetConnectionString(configuration, "Catalog"));
        services.AddCartInfrastructure(configuration);
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
