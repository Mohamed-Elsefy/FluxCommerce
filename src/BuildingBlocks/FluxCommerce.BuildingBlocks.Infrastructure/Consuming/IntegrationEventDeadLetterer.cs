using System.Threading.RateLimiting;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// RabbitMQ implementation of <see cref="IIntegrationEventDeadLetterer"/>.
/// <para>
/// Dead-lettering is rare (terminal failures only), but each publish is still confirmed: the dead-letter
/// copy is sent on a lazily-created confirm-enabled channel (declaring the durable topic DLX so the
/// topology self-heals) and <c>BasicPublishAsync</c> returns success only after the broker confirms it.
/// The copy preserves the original body and headers and carries the terminal-failure metadata supplied
/// by the processor, so the DLQ is a recoverable, diagnosable record of every terminal failure — the
/// full message is never lost, and no payload is logged (only metadata headers are written).
/// </para>
/// </summary>
public sealed class IntegrationEventDeadLetterer : IIntegrationEventDeadLetterer, IAsyncDisposable
{
    private readonly IRabbitMQConnectionProvider _connectionProvider;
    private readonly ILogger<IntegrationEventDeadLetterer> _logger;
    private readonly string _deadLetterExchange;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IChannel? _channel;

    public IntegrationEventDeadLetterer(
        IRabbitMQConnectionProvider connectionProvider,
        ILogger<IntegrationEventDeadLetterer> logger,
        string deadLetterExchange)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deadLetterExchange = string.IsNullOrWhiteSpace(deadLetterExchange)
            ? throw new ArgumentException("A dead-letter exchange name must be provided.", nameof(deadLetterExchange))
            : deadLetterExchange;
    }

    public string DeadLetterExchange => _deadLetterExchange;

    public async Task<bool> PublishAsync(
        IReadOnlyBasicProperties originalProperties,
        ReadOnlyMemory<byte> body,
        string routingKey,
        IReadOnlyDictionary<string, object?> additionalHeaders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalProperties);
        ArgumentNullException.ThrowIfNull(additionalHeaders);

        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
        var properties = BuildDeadLetterProperties(originalProperties, additionalHeaders);

        try
        {
            await channel.BasicPublishAsync(
                exchange: _deadLetterExchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (PublishException exception)
        {
            _logger.LogError(
                exception,
                "The dead-letter copy for routing key {RoutingKey} was not confirmed by the broker; the processor will fall back to rejecting the original delivery (Nack requeue:false).",
                routingKey);
            return false;
        }
    }

    /// <summary>
    /// Builds the dead-letter copy: the original AMQP properties (MessageId, Type, CorrelationId,
    /// Timestamp, persistent delivery) with the original transport headers preserved and the terminal
    /// failure headers merged on top.
    /// </summary>
    internal static BasicProperties BuildDeadLetterProperties(
        IReadOnlyBasicProperties originalProperties,
        IReadOnlyDictionary<string, object?> additionalHeaders)
    {
        var headers = new Dictionary<string, object?>();
        if (originalProperties.Headers is not null)
        {
            foreach (var pair in originalProperties.Headers)
                headers[pair.Key] = pair.Value;
        }

        foreach (var pair in additionalHeaders)
            headers[pair.Key] = pair.Value;

        return new BasicProperties
        {
            MessageId = originalProperties.MessageId,
            Type = originalProperties.Type,
            CorrelationId = originalProperties.CorrelationId,
            Timestamp = originalProperties.Timestamp,
            Persistent = true,
            Headers = headers
        };
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        await _channelLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true,
                outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(1));

            var channel = await _connectionProvider
                .CreateChannelAsync(channelOptions, cancellationToken)
                .ConfigureAwait(false);

            await channel.ExchangeDeclareAsync(
                exchange: _deadLetterExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null!,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _channel = channel;
            return channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channelLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
            }
        }
        finally
        {
            _channelLock.Release();
        }
    }
}
