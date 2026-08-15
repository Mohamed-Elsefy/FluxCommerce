using RabbitMQ.Client;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Messaging;

public interface IRabbitMQConnectionProvider : IAsyncDisposable
{
    IConnection GetConnection();

    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    Task<IChannel> CreateChannelAsync(CreateChannelOptions options, CancellationToken cancellationToken = default);
}
