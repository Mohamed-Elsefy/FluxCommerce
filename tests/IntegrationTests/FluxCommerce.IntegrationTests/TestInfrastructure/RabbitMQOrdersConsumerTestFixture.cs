using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Phase 7.9 consumer integration fixture. Shares the real SQL Server orders database (for the
/// idempotent-processing boundary and the projection read model) and the real RabbitMQ broker (for
/// delivery) on the dedicated test exchange, and fails fast when either is unreachable.
///
/// Each test builds its own consumer provider through <see cref="BuildConsumerProvider"/> with a
/// unique queue name so parallel collections cannot interfere. Orders are seeded with
/// <see cref="SeedOrderAsync"/> (hydrated aggregates, so no domain events and no outbox side effects),
/// and the real <c>OrderSummaryProjectionConsumer</c> is registered against the consumer's topic
/// exchange.
/// </summary>
public sealed class RabbitMQOrdersConsumerTestFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    public const string SqlServerConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Orders;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private const string DefaultRabbitMQConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _migrationProvider;

    public RabbitMQOrdersConsumerTestFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(SqlServerConnectionString);

        _migrationProvider = services.BuildServiceProvider();

        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        context.Database.Migrate();

        VerifyConnectivity();
    }

    public string RabbitMQConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) ?? DefaultRabbitMQConnection;

    public IServiceProvider Services => _migrationProvider;

    /// <summary>
    /// Builds a consumer host with the orders infrastructure (DbContext + stores + idempotency),
    /// RabbitMQ publishing and consuming on the test exchange, and the real Order Summary projection
    /// consumer. The hosted <c>RabbitMQConsumerBackgroundService</c> is started per test so the outbox
    /// hosted services never run.
    /// </summary>
    public ServiceProvider BuildConsumerProvider(
        string queueName,
        Action<FluxCommerce.BuildingBlocks.Infrastructure.Consuming.RabbitMQConsumerOptions>? configure = null,
        IntegrationTestCapturingLogger<IntegrationEventMessageProcessor>? capturedLogger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (capturedLogger is not null)
            services.AddSingleton<ILogger<IntegrationEventMessageProcessor>>(capturedLogger);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(SqlServerConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, OrderSummaryProjectionConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Phase 8.5: builds a consumer host that registers both Inventory result consumers
    /// (<c>InventoryReservedConsumer</c> and <c>InventoryReservationFailedConsumer</c>) against
    /// <c>inventory.reserved</c> / <c>inventory.reservation_failed</c> on the given queue, inside the
    /// Orders idempotent-processing boundary. The hosted <c>RabbitMQConsumerBackgroundService</c> is
    /// started per test so the outbox hosted services never run.
    /// </summary>
    public ServiceProvider BuildResultConsumerProvider(
        string queueName,
        Action<FluxCommerce.BuildingBlocks.Infrastructure.Consuming.RabbitMQConsumerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(SqlServerConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<InventoryReservedIntegrationEvent, InventoryReservedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationFailedIntegrationEvent, InventoryReservationFailedConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds an order with a single line item (10.00 USD x 2) directly in the orders database. The
    /// aggregate is hydrated (no domain events) so seeding produces no outbox side effects — the
    /// projection consumer reacts only to the event the test publishes.
    /// </summary>
    public async Task<Order> SeedOrderAsync(Guid orderId, Guid customerId)
    {
        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = Order.Hydrate(
            new OrderId(orderId),
            new CustomerId(customerId),
            new OrderNumber($"ORD-PROJ-{Guid.NewGuid():N}"[..Math.Min(32, 9 + 12)]),
            CreateAddress(),
            CreateAddress(),
            OrderStatus.Pending,
            Order.InitialVersion,
            new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku("SKU-PROJ"),
                    "Projection Test Product",
                    new OrderMoney(10.00m, "USD"),
                    2),
            ]);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }

    public async ValueTask DisposeAsync()
    {
        await _migrationProvider.DisposeAsync();
    }

    private static OrderAddress CreateAddress() =>
        new("Projection Recipient", "1 Projection St", null, "Projection City", "Region", "1000", "US");

    private void VerifyConnectivity()
    {
        try
        {
            var provider = new RabbitMQConnectionProvider(RabbitMQConnectionString);
            var channel = provider.CreateChannelAsync().GetAwaiter().GetResult();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "RabbitMQ orders consumer integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
                $"Start it with 'docker compose up -d rabbitmq' or override the connection through the " +
                $"'{ConnectionEnvironmentVariable}' environment variable. Underlying error: {exception.Message}",
                exception);
        }
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public bool IsAuthenticated => false;
    }
}