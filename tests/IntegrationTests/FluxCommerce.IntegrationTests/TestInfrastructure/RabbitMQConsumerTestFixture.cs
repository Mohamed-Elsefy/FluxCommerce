using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Phase 7.5 consumer integration fixture. Shares the real SQL Server inventory database (for the
/// idempotent-processing boundary) and the real RabbitMQ broker (for delivery) on the dedicated test
/// exchange, and fails fast when either is unreachable.
///
/// Each test builds its own provider through <see cref="BuildConsumerProvider{THandler}"/> with a
/// unique queue name so parallel collections cannot interfere; tests register a single event type
/// (<c>orders.order_created</c>) against the consumer's topic exchange.
/// </summary>
public sealed class RabbitMQConsumerTestFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    public const string SqlServerConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Inventory;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private const string DefaultRabbitMQConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _migrationProvider;

    public RabbitMQConsumerTestFixture()
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
    /// Builds a consumer host with the module's infrastructure (inventory + idempotency), RabbitMQ
    /// publishing and consuming on the test exchange, and a single event-type consumer. The hosted
    /// <c>RabbitMQConsumerBackgroundService</c> is started per test so the reservation-expiration and
    /// outbox hosted services never run.
    /// </summary>
    public ServiceProvider BuildConsumerProvider<THandler>(
        string queueName,
        TestConsumerRecorder recorder,
        Action<FluxCommerce.BuildingBlocks.Infrastructure.Consuming.RabbitMQConsumerOptions>? configure = null)
        where THandler : class, IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddInventoryInfrastructure(SqlServerConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddSingleton(recorder);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, THandler>();
        return services.BuildServiceProvider();
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
                "RabbitMQ consumer integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
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