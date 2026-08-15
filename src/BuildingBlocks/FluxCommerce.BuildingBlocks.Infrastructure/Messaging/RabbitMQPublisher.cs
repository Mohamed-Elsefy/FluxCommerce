using System.Text.Json;
using System.Threading.RateLimiting;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ implementation of <see cref="IRabbitMQPublisher"/>.
/// <para>
/// Each publish runs on a freshly created channel configured with publisher confirms
/// (<see cref="CreateChannelOptions.PublisherConfirmationsEnabled"/> + tracking). With confirmation
/// tracking enabled, awaiting <c>BasicPublishAsync</c> completes only when the broker confirms the
/// message and throws <see cref="PublishException"/> (nack or return) otherwise — the client waits
/// for the broker response, so success is never reported for a mere publish attempt.
/// </para>
/// <para>
/// The exchange is declared idempotently (durable topic exchange) on every publish so the topology
/// self-heals and the publisher needs no external wiring. Metadata is carried both as AMQP properties
/// (<c>MessageId</c>, <c>Type</c>, <c>CorrelationId</c>, <c>Timestamp</c>, persistent delivery) and as
/// transport headers (<c>EventId</c>, <c>EventType</c>, <c>EventVersion</c>, <c>CorrelationId</c>,
/// <c>CausationId</c>, <c>OccurredAtUtc</c>).
/// </para>
/// </summary>
public sealed class RabbitMQPublisher : IRabbitMQPublisher
{
    private readonly IRabbitMQConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly string _exchangeName;

    public RabbitMQPublisher(IRabbitMQConnectionProvider connectionProvider, ILogger<RabbitMQPublisher> logger, string exchangeName)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = string.IsNullOrWhiteSpace(exchangeName)
            ? throw new ArgumentException("A RabbitMQ exchange name must be provided.", nameof(exchangeName))
            : exchangeName;
    }

    public string ExchangeName => _exchangeName;

    public async Task<bool> PublishAsync(string eventType, IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("An event type must be provided.", nameof(eventType));

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(1));

        await using var channel = await _connectionProvider.CreateChannelAsync(channelOptions, cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null!,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);

        var body = SerializeBody(@event);
        var properties = BuildProperties(eventType, @event);

        try
        {
            await channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: eventType,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        catch (PublishException publishException)
        {
            _logger.LogError(
                publishException,
                "RabbitMQ did not confirm the {EventType} event {EventId}; the broker nacked or returned the message.",
                eventType,
                @event.EventId);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Serializes the event from its runtime concrete type so the full business payload and the
    /// inherited metadata are written, using the default (PascalCase) options that match the Outbox
    /// payload contract.
    /// </summary>
    internal static byte[] SerializeBody(IIntegrationEvent @event) => JsonSerializer.SerializeToUtf8Bytes((object)@event);

    internal static BasicProperties BuildProperties(string eventType, IIntegrationEvent @event)
    {
        return new BasicProperties
        {
            MessageId = @event.EventId.ToString(),
            Type = eventType,
            CorrelationId = @event.CorrelationId.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAtUtc).ToUnixTimeSeconds()),
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                ["EventId"] = @event.EventId.ToString(),
                ["EventType"] = eventType,
                ["EventVersion"] = @event.Version.ToString(),
                ["CorrelationId"] = @event.CorrelationId.ToString(),
                ["CausationId"] = @event.CausationId?.ToString() ?? string.Empty,
                ["OccurredAtUtc"] = @event.OccurredAtUtc.ToString("O")
            }
        };
    }
}
