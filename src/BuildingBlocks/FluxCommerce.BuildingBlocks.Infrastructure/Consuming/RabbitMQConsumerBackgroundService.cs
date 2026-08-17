using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Phase 7.5/7.6 RabbitMQ consumer host. It consumes from the SAME Phase 6 topology (the durable topic
/// exchange) instead of creating a parallel messaging architecture: on startup it declares the
/// exchange and the configured queue, binds the queue to each registered event type's routing key,
/// applies per-channel prefetch, and starts <see cref="RabbitMQConsumerOptions.Concurrency"/> consumers
/// against the queue (the broker round-robins deliveries across them, giving bounded in-flight
/// concurrency). Every delivery is processed in its own DI scope through
/// <see cref="IIntegrationEventMessageProcessor"/> and acknowledged manually — only after processing
/// completes. Shutdown cancels in-flight processing (leaving uncommitted deliveries unacknowledged so
/// the broker redelivers them, at-least-once), cancels the consumers, awaits in-flight work, and then
/// disposes the channels.
///
/// <para>
/// Phase 7.6 dead-letter topology: the main queue is declared with an <c>x-dead-letter-exchange</c>
/// pointing at a dedicated dead-letter exchange ({QueueName}.dlx by default), which routes terminally
/// failed deliveries — malformed messages, permanent failures, and exhausted retries — to a dedicated
/// dead-letter queue ({QueueName}.dlq by default) so no message is silently lost and terminal failures
/// remain diagnosable. Reject/DeadLetter are applied as Nack(requeue:false), which the broker routes to
/// the dead-letter queue; the host-level fallback for unexpected delivery errors remains Nack(requeue:true).
/// </para>
/// </summary>
public sealed class RabbitMQConsumerBackgroundService : BackgroundService
{
    private readonly IRabbitMQConnectionProvider _connectionProvider;
    private readonly IntegrationEventConsumerRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RabbitMQConsumerOptions> _options;
    private readonly ILogger<RabbitMQConsumerBackgroundService> _logger;

    private readonly List<ConsumerChannel> _channels = new();
    private readonly object _channelsLock = new();
    private readonly List<Task> _inFlightDeliveries = new();
    private readonly object _inFlightLock = new();
    private readonly CancellationTokenSource _processingCts = new();

    public RabbitMQConsumerBackgroundService(
        IRabbitMQConnectionProvider connectionProvider,
        IntegrationEventConsumerRegistry registry,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMQConsumerOptions> options,
        ILogger<RabbitMQConsumerBackgroundService> logger)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        ValidateOptions(options);

        var descriptors = _registry.GetDescriptors();

        if (descriptors.Count == 0)
        {
            _logger.LogWarning(
                "RabbitMQ consumer for queue {Queue} has no registered integration event consumers; no routing bindings will be created.",
                options.QueueName);
        }

        for (var index = 0; index < options.Concurrency; index++)
        {
            var consumer = await StartConsumerChannelAsync(options, descriptors, stoppingToken).ConfigureAwait(false);

            lock (_channelsLock)
            {
                _channels.Add(consumer);
            }
        }

        _logger.LogInformation(
            "RabbitMQ consumer host is running {Concurrency} consumer(s) on queue {Queue} (exchange {Exchange}).",
            options.Concurrency,
            options.QueueName,
            options.ExchangeName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested; StopAsync handles consumer cancellation and channel disposal.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _processingCts.Cancel();

        ConsumerChannel[] channels;
        lock (_channelsLock)
        {
            channels = _channels.ToArray();
        }

        // Stop accepting new deliveries before waiting for in-flight work to finish.
        foreach (var consumer in channels)
        {
            try
            {
                await consumer.Channel
                    .BasicCancelAsync(consumer.ConsumerTag, noWait: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or AlreadyClosedException or ObjectDisposedException)
            {
                // The channel may already be gone (connection loss); nothing to cancel.
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Await in-flight deliveries: cancellation is observed and they complete without acking, so
        // the broker redelivers the uncommitted messages after the channels close.
        Task[] inFlight;
        lock (_inFlightLock)
        {
            inFlight = _inFlightDeliveries.ToArray();
        }

        if (inFlight.Length > 0)
        {
            try
            {
                await Task.WhenAll(inFlight).ConfigureAwait(false);
            }
            catch
            {
                // In-flight work observes cancellation; per-delivery failures were already handled.
            }
        }

        lock (_channelsLock)
        {
            channels = _channels.ToArray();
            _channels.Clear();
        }

        foreach (var consumer in channels)
        {
            await consumer.Channel.DisposeAsync().ConfigureAwait(false);
        }

        _processingCts.Dispose();
    }

    private async Task<ConsumerChannel> StartConsumerChannelAsync(
        RabbitMQConsumerOptions options,
        IReadOnlyList<IntegrationEventConsumerDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        var channel = await _connectionProvider
            .CreateChannelAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var deadLetterExchange = options.ResolvedDeadLetterExchangeName;
            var deadLetterQueue = options.ResolvedDeadLetterQueueName;

            // Dead-letter topology for terminal failures (malformed, permanent, exhausted retries):
            // the main queue's x-dead-letter-exchange routes rejected deliveries to the DLQ.
            await channel.ExchangeDeclareAsync(
                exchange: deadLetterExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null!,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueDeclareAsync(
                queue: deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null!,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueBindAsync(
                queue: deadLetterQueue,
                exchange: deadLetterExchange,
                routingKey: "#",
                arguments: null!,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueDeclareAsync(
                queue: options.QueueName,
                durable: options.Durable,
                exclusive: false,
                autoDelete: options.AutoDelete,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = deadLetterExchange
                },
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var descriptor in descriptors)
            {
                await channel.QueueBindAsync(
                    queue: options.QueueName,
                    exchange: options.ExchangeName,
                    routingKey: descriptor.EventType,
                    arguments: null!,
                    noWait: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: options.PrefetchCount,
                global: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (sender, args) => HandleReceivedAsync(channel, args);

            var consumerTag = await channel
                .BasicConsumeAsync(
                    queue: options.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "RabbitMQ consumer {ConsumerTag} started on queue {Queue} (exchange {Exchange}, prefetch {PrefetchCount}).",
                consumerTag,
                options.QueueName,
                options.ExchangeName,
                options.PrefetchCount);

            return new ConsumerChannel(channel, consumerTag);
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task HandleReceivedAsync(IChannel channel, BasicDeliverEventArgs args)
    {
        var delivery = ProcessDeliveryAsync(channel, args, _processingCts.Token);

        lock (_inFlightLock)
        {
            _inFlightDeliveries.Add(delivery);
        }

        try
        {
            await delivery.ConfigureAwait(false);
        }
        finally
        {
            lock (_inFlightLock)
            {
                _inFlightDeliveries.Remove(delivery);
            }
        }
    }

    private async Task ProcessDeliveryAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();
            var action = await processor
                .ProcessAsync(args.BasicProperties, args.Body, cancellationToken)
                .ConfigureAwait(false);

            switch (action)
            {
                case ConsumerMessageAction.Ack:
                    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                    break;
                case ConsumerMessageAction.Requeue:
                    await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken).ConfigureAwait(false);
                    break;
                case ConsumerMessageAction.Reject:
                    await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
                    break;
                case ConsumerMessageAction.DeadLetter:
                    await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown consumer message action.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress: leave the delivery unacknowledged so the broker redelivers it once
            // the channel closes (at-least-once). Cancelled work never commits and never acks.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Consumer failed to handle delivery {DeliveryTag}; requeueing the message.", args.DeliveryTag);

            try
            {
                await channel
                    .BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ackException)
            {
                _logger.LogWarning(
                    ackException,
                    "Consumer could not requeue delivery {DeliveryTag}; unacknowledged messages are redelivered after the channel closes.",
                    args.DeliveryTag);
            }
        }
    }

    private static void ValidateOptions(RabbitMQConsumerOptions options)
    {
        // Consolidates Phase 7.5/7.6/7.7 configuration validation (required values, retry policy shape,
        // dead-letter topology not aliasing the main topology) so misconfiguration fails fast on startup.
        options.Validate();
    }

    private sealed record ConsumerChannel(IChannel Channel, string ConsumerTag);
}