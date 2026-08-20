using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ implementation of <see cref="IRabbitMQPublisher"/>.
/// <para>
/// Publish runs on a single lazily-created channel configured with publisher confirms
/// (<see cref="CreateChannelOptions.PublisherConfirmationsEnabled"/> + tracking) that is reused across
/// publishes instead of opening, declaring, and closing a channel per message. A closed channel is
/// transparently recreated on the next publish, so the exchange stays self-healing (declared on every
/// channel (re)creation) without a per-message topology round trip. The confirm tracking
/// (<see cref="CreateChannelOptions.PublisherConfirmationTrackingEnabled"/>) means awaiting
/// <c>BasicPublishAsync</c> completes only when the broker confirms the message and throws
/// <see cref="PublishException"/> (nack or return) otherwise — the client waits for the broker response,
/// so success is never reported for a mere publish attempt.
/// </para>
/// <para>
/// Metadata is carried both as AMQP properties (<c>MessageId</c>, <c>Type</c>, <c>CorrelationId</c>,
/// <c>Timestamp</c>, persistent delivery) and as transport headers (<c>EventId</c>, <c>EventType</c>,
/// <c>EventVersion</c>, <c>CorrelationId</c>, <c>CausationId</c>, <c>OccurredAtUtc</c>). Phase 8.9 adds the
/// W3C Trace Context headers (<c>traceparent</c>/<c>tracestate</c>) captured from the current activity, so
/// the distributed trace survives the RabbitMQ hop.
/// </para>
/// </summary>
public sealed class RabbitMQPublisher : IRabbitMQPublisher, IAsyncDisposable
{
    private readonly IRabbitMQConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly string _exchangeName;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IChannel? _channel;

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

        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);

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
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (PublishException publishException)
        {
            _logger.LogError(
                publishException,
                "RabbitMQ did not confirm the {EventType} event {EventId} (correlation {CorrelationId}); the broker nacked or returned the message.",
                eventType,
                @event.EventId,
                @event.CorrelationId);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Lazily creates the shared confirm-enabled channel, declares the durable topic exchange, and
    /// reuses it for every publish. A channel that the broker has closed is replaced on the next
    /// publish (with a fresh exchange declaration), so the topology self-heals without a per-message
    /// declaration round trip. Confirms are preserved: each publish still awaits its own broker confirm.
    /// </summary>
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
                exchange: _exchangeName,
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

    /// <summary>
    /// Serializes the event from its runtime concrete type so the full business payload and the
    /// inherited metadata are written, using the default (PascalCase) options that match the Outbox
    /// payload contract.
    /// </summary>
    internal static byte[] SerializeBody(IIntegrationEvent @event) => JsonSerializer.SerializeToUtf8Bytes((object)@event);

    internal static BasicProperties BuildProperties(string eventType, IIntegrationEvent @event)
    {
        var headers = new Dictionary<string, object?>
        {
            ["EventId"] = @event.EventId.ToString(),
            ["EventType"] = eventType,
            ["EventVersion"] = @event.Version.ToString(),
            ["CorrelationId"] = @event.CorrelationId.ToString(),
            ["CausationId"] = @event.CausationId?.ToString() ?? string.Empty,
            ["OccurredAtUtc"] = @event.OccurredAtUtc.ToString("O")
        };

        // Phase 8.9: propagate the W3C Trace Context of the current activity (the outbox dispatch producer
        // span) so the consumer can continue the same trace. Only W3C ids are propagated (W3C is the
        // default in .NET), and only a non-empty trace state is sent.
        var traceParent = TraceContext.GetTraceParent(Activity.Current);
        if (traceParent is not null)
            headers[TraceContext.TraceParentHeader] = traceParent;

        var traceState = TraceContext.GetTraceState(Activity.Current);
        if (traceState is not null)
            headers[TraceContext.TraceStateHeader] = traceState;

        return new BasicProperties
        {
            MessageId = @event.EventId.ToString(),
            Type = eventType,
            CorrelationId = @event.CorrelationId.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAtUtc).ToUnixTimeSeconds()),
            Persistent = true,
            Headers = headers
        };
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
