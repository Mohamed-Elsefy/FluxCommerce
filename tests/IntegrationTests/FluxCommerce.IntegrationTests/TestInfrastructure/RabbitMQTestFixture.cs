using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Shared, fail-fast RabbitMQ fixture for the publisher integration test collection.
///
/// Lifecycle (mirrors the Redis/SQL integration fixtures):
///   1. Initialize — resolve the connection string (FLUXCOMMERCE_RABBITMQ_CONNECTION, default
///                   the local docker-compose broker with the .env credentials).
///   2. Wire       — build a service collection with AddRabbitMQ (connection) and
///                   AddRabbitMQPublisher (a dedicated test exchange, not the production one).
///   3. Verify     — create a channel. RabbitMQ tests FAIL FAST (throw) when the broker is
///                   unreachable so the suite never silently passes without a real broker.
///   4. Run tests  — tests exercise RabbitMQPublisher against the real broker (confirms, delivery,
///                   routing, metadata).
///   5. Cleanup    — dispose the container, which disposes the connection provider's connection.
/// </summary>
public sealed class RabbitMQTestFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    private const string DefaultConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _serviceProvider;

    public RabbitMQTestFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) ?? DefaultConnection;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMQ(connectionString);
        services.AddRabbitMQPublisher(TestExchangeName);

        _serviceProvider = services.BuildServiceProvider();

        Publisher = _serviceProvider.GetRequiredService<IRabbitMQPublisher>();
        Provider = _serviceProvider.GetRequiredService<IRabbitMQConnectionProvider>();

        VerifyConnectivity();
    }

    public IRabbitMQPublisher Publisher { get; }

    public IRabbitMQConnectionProvider Provider { get; }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
    }

    private void VerifyConnectivity()
    {
        try
        {
            var channel = Provider.CreateChannelAsync().GetAwaiter().GetResult();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "RabbitMQ integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
                $"Start it with 'docker compose up -d rabbitmq' or override the connection through the " +
                $"'{ConnectionEnvironmentVariable}' environment variable. Underlying error: {exception.Message}",
                exception);
        }
    }
}