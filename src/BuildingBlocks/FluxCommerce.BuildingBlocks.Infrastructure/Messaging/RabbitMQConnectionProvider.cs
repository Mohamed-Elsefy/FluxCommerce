using RabbitMQ.Client;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Messaging;

public sealed class RabbitMQConnectionProvider : IRabbitMQConnectionProvider
{
    private readonly Lazy<Task<IConnection>> _connection;

    public RabbitMQConnectionProvider(string connectionString)
    {
        _connection = new Lazy<Task<IConnection>>(() => CreateConnectionAsync(connectionString));
    }

    public IConnection GetConnection()
    {
        return _connection.Value.GetAwaiter().GetResult();
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connection.Value;
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            var connection = await _connection.Value;
            await connection.DisposeAsync();
        }
    }

    private static Task<IConnection> CreateConnectionAsync(string connectionString)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true
        };

        return factory.CreateConnectionAsync();
    }
}
