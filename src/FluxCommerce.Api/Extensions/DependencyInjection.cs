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
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
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
        services.AddInventoryInfrastructure(
            GetConnectionString(configuration, "Inventory"),
            expirationOptions => configuration
                .GetSection(ReservationExpirationOptions.SectionName)
                .Bind(expirationOptions));
        services.AddPaymentsInfrastructure(GetConnectionString(configuration, "Payments"));
        services.AddIdentityInfrastructure(GetConnectionString(configuration, "Identity"));

        // Web-backed correlation provider overrides the modules' fallback (last registration wins),
        // so Outbox records written inside HTTP requests carry the request's X-Correlation-ID.
        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationIdProvider, WebCorrelationIdProvider>();

        services.AddRedis(GetRedisConnectionString(configuration));
        services.AddRabbitMQ(GetRabbitMQConnectionString(configuration));
        services.AddRabbitMQPublisher(GetRabbitMQExchangeName(configuration));

        // Phase 7.9: the first real integration event consumer. The Order Summary projection consumer
        // consumes orders.order_created from the same Phase 6 topic exchange through the dedicated
        // module queue, inside the Orders idempotent-processing boundary (Business State + ProcessedMessage
        // commit atomically). The processor is declared explicitly on the consumer so it always executes
        // through the Orders boundary — never through a container-wide IIdempotentProcessor, which
        // Inventory (registered last) also registers.
        services.AddRabbitMQConsumer(GetRabbitMQExchangeName(configuration), "q.orders.summary");
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, OrderSummaryProjectionConsumer, OrdersIdempotentProcessor>();

        // Phase 8.4: the Inventory reservation-request consumer. The Orders saga (8.3) stages one
        // inventory.reservation_requested per order line item through the Orders outbox; the Inventory
        // consumer executes the reservation through the Inventory idempotent-processing boundary
        // (Business State + ProcessedMessage commit atomically), declared on the descriptor exactly
        // like the summary consumer on the Orders side.
        services.AddRabbitMQConsumer(GetRabbitMQExchangeName(configuration), "q.inventory.reservation");
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();

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

    private static string GetRabbitMQExchangeName(IConfiguration configuration)
    {
        return configuration.GetSection(MessageBrokerOptions.SectionName).Get<MessageBrokerOptions>()?.ExchangeName
            ?? throw new InvalidOperationException("RabbitMQ exchange name is not configured.");
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
