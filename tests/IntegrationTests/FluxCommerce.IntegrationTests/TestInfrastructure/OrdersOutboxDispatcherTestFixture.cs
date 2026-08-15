using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Host for the Phase 6.7/6.8 Orders outbox dispatcher integration tests. The real SQL Server
/// container provides the atomic claim/recover/complete semantics (including the Phase 6.8 backoff
/// window) and the real RabbitMQ broker verifies the end-to-end publish (routing key, exchange,
/// metadata). The dispatcher runs on the dedicated test exchange, never the production one. Tests
/// drive <c>OrdersOutboxDispatcher.DispatchPendingAsync</c> directly; the hosted-service loop itself is
/// thin glue over that cycle. The retry policy is tuned for fast tests: three retries with a 2-second
/// base backoff capped at 10 seconds.
/// </summary>
public sealed class OrdersOutboxDispatcherTestFixture : IAsyncDisposable
{
    public const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Orders;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public const string RabbitMQConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    private const string DefaultRabbitMQConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _provider;

    public OrdersOutboxDispatcherTestFixture()
    {
        var rabbitMqConnection = Environment.GetEnvironmentVariable(RabbitMQConnectionEnvironmentVariable)
            ?? DefaultRabbitMQConnection;

        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddOrdersInfrastructure(ConnectionString);

        services.AddLogging();
        services.AddRabbitMQ(rabbitMqConnection);
        services.AddRabbitMQPublisher(TestExchangeName);

        services.Configure<OutboxDispatchOptions>(options =>
        {
            options.BatchSize = 50;
            options.PollingInterval = TimeSpan.FromSeconds(1);
            options.ClaimTimeout = TimeSpan.FromSeconds(5);
            options.MaxRetryCount = 3;
            options.BaseRetryDelay = TimeSpan.FromSeconds(2);
            options.RetryDelayMultiplier = 2;
            options.MaxRetryDelay = TimeSpan.FromSeconds(10);
        });

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        context.Database.Migrate();

        VerifyConnectivity();
    }

    public IServiceProvider Services => _provider;

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }

    private void VerifyConnectivity()
    {
        try
        {
            var provider = _provider.GetRequiredService<IRabbitMQConnectionProvider>();
            var channel = provider.CreateChannelAsync().GetAwaiter().GetResult();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Orders outbox dispatcher integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
                "Start it with 'docker compose up -d rabbitmq' or override the connection through the " +
                $"'{RabbitMQConnectionEnvironmentVariable}' environment variable. Underlying error: {exception.Message}",
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