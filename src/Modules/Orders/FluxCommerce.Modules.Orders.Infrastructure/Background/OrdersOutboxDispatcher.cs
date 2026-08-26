using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Dispatch;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Orders.Infrastructure.Background;

/// <summary>
/// Phase 6.7/6.8 background processor that dispatches the Orders outbox to the message broker. Each
/// cycle recovers stale InProgress claims, atomically claims a bounded batch, deserializes each payload
/// to its integration contract, publishes it through <see cref="IRabbitMQPublisher"/>, and marks it
/// published. A failed publication never loses the message: retryable failures are returned to Pending
/// with the retry count, error, and a backoff deadline recorded so a later cycle re-claims the message
/// only once its backoff window has elapsed; unknown event types, corrupt payloads, and failures that
/// exhaust the retry budget move the message to the terminal Failed state instead of being retried
/// forever. Shutdown cancels the current publish and leaves the claim InProgress for stale-claim
/// recovery.
/// </summary>
public sealed class OrdersOutboxDispatcher : BackgroundService, IOutboxDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxDispatchOptions> _options;
    private readonly ILogger<OrdersOutboxDispatcher> _logger;

    public OrdersOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatchOptions> options,
        ILogger<OrdersOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Transient failures must not stop the dispatcher; the next cycle retries.
                _logger.LogError(exception, "Outbox dispatch cycle failed and will be retried.");
            }

            try
            {
                await Task.Delay(_options.Value.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one dispatch cycle inside a fresh scope: recovers stale claims and dispatches a single
    /// batch. The hosted-service loop calls it on each tick; tests call it directly.
    /// </summary>
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<OrdersOutboxStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRabbitMQPublisher>();

        await DispatchCoreAsync(store, publisher, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches one batch through explicitly supplied store and publisher. Internal so unit tests
    /// can drive the exact claim → publish → mark flow with mocks.
    /// </summary>
    internal async Task DispatchCoreAsync(
        IOutboxStore store,
        IRabbitMQPublisher publisher,
        CancellationToken cancellationToken)
    {
        var recovered = await store
            .RecoverExpiredClaimsAsync(_options.Value.ClaimTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (recovered > 0)
            _logger.LogInformation("Recovered {RecoveredCount} stale outbox claim(s).", recovered);

        var claimed = await store
            .ClaimPendingAsync(_options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in claimed)
        {
            await DispatchMessageAsync(message, store, publisher, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchMessageAsync(
        OutboxMessage message,
        IOutboxStore store,
        IRabbitMQPublisher publisher,
        CancellationToken cancellationToken)
    {
        // Phase 8.9: the dispatch producer span is rooted at the trace context captured when the message
        // was staged, so the outbox hop keeps the workflow on the same trace. The publisher injects the
        // span id into the transport traceparent header for the consumer to continue from.
        using var activity = TraceContext.StartActivity(
            "Outbox.Dispatch",
            ActivityKind.Producer,
            message.TraceParent,
            message.TraceState);

        activity?
            .AddTag("outbox.message.id", message.Id)
            .AddTag("event.type", message.Type)
            .AddTag("event.id", message.EventId)
            .AddTag("correlation.id", message.CorrelationId);

        try
        {
            var integrationEvent = Deserialize(message);

            if (integrationEvent is null)
            {
                await HandleFailureAsync(
                    message,
                    store,
                    $"Unsupported outbox event type '{message.Type}'.",
                    retryable: false,
                    cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var published = await publisher
                .PublishAsync(message.Type, integrationEvent, cancellationToken)
                .ConfigureAwait(false);

            if (!published)
            {
                await HandleFailureAsync(
                    message,
                    store,
                    "The message broker did not confirm the publication.",
                    retryable: true,
                    cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await store.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Outbox message {MessageId} published {EventType} event {EventId} (correlation {CorrelationId}) on attempt {Attempt}.",
                message.Id,
                message.Type,
                message.EventId,
                message.CorrelationId,
                message.RetryCount + 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress: leave the claim InProgress so stale-claim recovery completes the
            // message later; a cancelled publish is never reported as a retryable failure.
            throw;
        }
        catch (JsonException exception)
        {
            // A payload that cannot be deserialized can never be published, so retrying cannot help;
            // fail it immediately rather than wasting the retry budget on a poison message.
            await HandleFailureAsync(
                message,
                store,
                $"The outbox payload could not be deserialized: {exception.Message}",
                retryable: false,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleFailureAsync(message, store, exception.Message, retryable: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the Phase 6.8 failure policy to a claimed message that failed to publish. Non-retryable
    /// failures (unknown event types, corrupt payloads) and failures that have exhausted the retry budget
    /// move the message to the terminal Failed state. Retryable failures return the message to Pending
    /// and schedule its next claim after an exponential backoff based on the current retry count.
    /// </summary>
    private async Task HandleFailureAsync(
        OutboxMessage message,
        IOutboxStore store,
        string error,
        bool retryable,
        CancellationToken cancellationToken)
    {
        if (!retryable || message.RetryCount >= _options.Value.MaxRetryCount)
        {
            await store.MarkFailedAsync(message.Id, error, cancellationToken).ConfigureAwait(false);

            if (retryable)
                _logger.LogError(
                    "Outbox message {MessageId} ({EventType}) exhausted its {MaxRetryCount} retries publishing event {EventId} (correlation {CorrelationId}) and failed terminally: {Error}",
                    message.Id,
                    message.Type,
                    _options.Value.MaxRetryCount,
                    message.EventId,
                    message.CorrelationId,
                    error);
            else
                _logger.LogError(
                    "Outbox message {MessageId} ({EventType}) failed publishing event {EventId} (correlation {CorrelationId}) and is not retryable: {Error}",
                    message.Id,
                    message.Type,
                    message.EventId,
                    message.CorrelationId,
                    error);

            return;
        }

        var retryAfterUtc = DateTime.UtcNow.Add(ComputeRetryDelay(message.RetryCount));

        await store
            .MarkRetryableFailureAsync(message.Id, error, retryAfterUtc, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogWarning(
            "Outbox message {MessageId} ({EventType}) failed publishing event {EventId} (correlation {CorrelationId}) on attempt {Attempt} and will be retried after {RetryAfterUtc}: {Error}",
            message.Id,
            message.Type,
            message.EventId,
            message.CorrelationId,
            message.RetryCount + 1,
            retryAfterUtc,
            error);
    }

    /// <summary>
    /// Computes the exponential backoff delay for a message that has already failed
    /// <paramref name="retryCount"/> times: <c>BaseRetryDelay * RetryDelayMultiplier^RetryCount</c>,
    /// capped at <see cref="OutboxDispatchOptions.MaxRetryDelay"/>.
    /// </summary>
    private TimeSpan ComputeRetryDelay(int retryCount)
    {
        var options = _options.Value;
        var delay = options.BaseRetryDelay;

        for (var attempt = 0; attempt < retryCount && delay < options.MaxRetryDelay; attempt++)
            delay = TimeSpan.FromTicks((long)(delay.Ticks * options.RetryDelayMultiplier));

        return delay > options.MaxRetryDelay ? options.MaxRetryDelay : delay;
    }

    private static IIntegrationEvent? Deserialize(OutboxMessage message) =>
        message.Type switch
        {
            OrderCreatedIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Payload),
            OrderCancelledIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(message.Payload),
            InventoryReservationRequestedIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<InventoryReservationRequestedIntegrationEvent>(message.Payload),
            InventoryReleaseRequestedIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<InventoryReleaseRequestedIntegrationEvent>(message.Payload),
            CreatePaymentRequestedIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<CreatePaymentRequestedIntegrationEvent>(message.Payload),
            _ => null
        };
}