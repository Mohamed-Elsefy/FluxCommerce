using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Phase 8.4 consumer integration fixture. Shares the real SQL Server inventory database (for the
/// idempotent-processing boundary and the reservation read state) and the real RabbitMQ broker (for
/// delivery) on the dedicated test exchange, and fails fast when either is unreachable.
///
/// Each test builds its own consumer provider through <see cref="BuildConsumerProvider"/> with a unique
/// queue name so parallel collections cannot interfere; tests register the real
/// <c>InventoryReservationRequestedConsumer</c> against <c>inventory.reservation_requested</c>. Items
/// are seeded with <see cref="SeedItemAsync"/> (hydrated aggregates, so no domain events and no outbox
/// side effects), and the hosted <c>RabbitMQConsumerBackgroundService</c> is started per test so the
/// outbox and reservation-expiration hosted services never run.
/// </summary>
public sealed class RabbitMQInventoryConsumerTestFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    public const string SqlServerConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Inventory;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private const string DefaultRabbitMQConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _migrationProvider;

    public RabbitMQInventoryConsumerTestFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddInventoryInfrastructure(SqlServerConnectionString);

        _migrationProvider = services.BuildServiceProvider();

        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        context.Database.Migrate();

        VerifyConnectivity();
    }

    public string RabbitMQConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) ?? DefaultRabbitMQConnection;

    public IServiceProvider Services => _migrationProvider;

    /// <summary>
    /// Builds a consumer host with the inventory infrastructure (DbContext + stores + idempotency),
    /// RabbitMQ publishing and consuming on the test exchange, and the real reservation-request
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
        services.AddInventoryInfrastructure(SqlServerConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds an inventory item with <paramref name="onHandQuantity"/> directly in the inventory
    /// database. The aggregate is hydrated (no domain events) so seeding produces no outbox side
    /// effects — the reservation consumer reacts only to the event the test publishes.
    /// </summary>
    public async Task SeedItemAsync(string sku, int onHandQuantity)
    {
        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var item = InventoryItem.Hydrate(
            new InventorySku(sku),
            onHandQuantity,
            reservedQuantity: 0,
            InventoryItem.InitialVersion,
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero));

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _migrationProvider.DisposeAsync();
    }

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
                "RabbitMQ inventory consumer integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
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

        public bool IsAuthenticated => false;
    }
}