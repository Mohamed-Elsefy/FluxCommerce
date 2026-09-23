using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Dispatch;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Payments.Infrastructure.Background;

/// <summary>
/// Phase 10.3 background dispatcher for the Payments outbox. Mirrors
/// <c>OrdersOutboxDispatcher</c>: recovers stale claims, atomically claims a batch,
/// deserializes each payload to its Payments contract, publishes via
/// <see cref="IRabbitMQPublisher"/>, and marks published. Retryable failures are
/// returned to Pending with exponential backoff; corrupt/unknown types go to Failed.
/// Enables reliable publication of <c>payments.payment_succeeded/failed</c> staged
/// by the webhook processor (Phase 9.11).
/// </summary>
public sealed class PaymentsOutboxDispatcher : BackgroundService, IOutboxDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxDispatchOptions> _options;
    private readonly ILogger<PaymentsOutboxDispatcher> _logger;

    public PaymentsOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatchOptions> options,
        ILogger<PaymentsOutboxDispatcher> logger)
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
                _logger.LogError(exception, "Payments outbox dispatch cycle failed and will be retried.");
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

    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PaymentsOutboxStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRabbitMQPublisher>();
        await DispatchCoreAsync(store, publisher, cancellationToken).ConfigureAwait(false);
    }

    internal async Task DispatchCoreAsync(IOutboxStore store, IRabbitMQPublisher publisher, CancellationToken cancellationToken)
    {
        var recovered = await store.RecoverExpiredClaimsAsync(_options.Value.ClaimTimeout, cancellationToken).ConfigureAwait(false);
        if (recovered > 0)
            _logger.LogInformation("Recovered {RecoveredCount} stale Payments outbox claim(s).", recovered);

        var claimed = await store.ClaimPendingAsync(_options.Value.BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var message in claimed)
            await DispatchMessageAsync(message, store, publisher, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchMessageAsync(OutboxMessage message, IOutboxStore store, IRabbitMQPublisher publisher, CancellationToken cancellationToken)
    {
        using var activity = TraceContext.StartActivity("Outbox.Dispatch", ActivityKind.Producer, message.TraceParent, message.TraceState);
        activity?.AddTag("outbox.message.id", message.Id)
            .AddTag("event.type", message.Type)
            .AddTag("event.id", message.EventId)
            .AddTag("correlation.id", message.CorrelationId);

        try
        {
            var integrationEvent = Deserialize(message);
            if (integrationEvent is null)
            {
                await HandleFailureAsync(message, store, $"Unsupported outbox event type '{message.Type}'.", retryable: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            var published = await publisher.PublishAsync(message.Type, integrationEvent, cancellationToken).ConfigureAwait(false);
            if (!published)
            {
                await HandleFailureAsync(message, store, "The message broker did not confirm the publication.", retryable: true, cancellationToken).ConfigureAwait(false);
                return;
            }

            await store.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Payments outbox message {MessageId} published {EventType} event {EventId} (correlation {CorrelationId}) on attempt {Attempt}.",
                message.Id, message.Type, message.EventId, message.CorrelationId, message.RetryCount + 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            await HandleFailureAsync(message, store, $"The outbox payload could not be deserialized: {exception.Message}", retryable: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleFailureAsync(message, store, exception.Message, retryable: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(OutboxMessage message, IOutboxStore store, string error, bool retryable, CancellationToken cancellationToken)
    {
        if (!retryable || message.RetryCount >= _options.Value.MaxRetryCount)
        {
            await store.MarkFailedAsync(message.Id, error, cancellationToken).ConfigureAwait(false);
            if (retryable)
                _logger.LogError("Payments outbox message {MessageId} ({EventType}) exhausted its {MaxRetryCount} retries publishing event {EventId} (correlation {CorrelationId}) and failed terminally: {Error}",
                    message.Id, message.Type, _options.Value.MaxRetryCount, message.EventId, message.CorrelationId, error);
            else
                _logger.LogError("Payments outbox message {MessageId} ({EventType}) failed publishing event {EventId} (correlation {CorrelationId}) and is not retryable: {Error}",
                    message.Id, message.Type, message.EventId, message.CorrelationId, error);
            return;
        }

        var retryAfterUtc = DateTime.UtcNow.Add(ComputeRetryDelay(message.RetryCount));
        await store.MarkRetryableFailureAsync(message.Id, error, retryAfterUtc, cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Payments outbox message {MessageId} ({EventType}) failed publishing event {EventId} (correlation {CorrelationId}) on attempt {Attempt} and will be retried after {RetryAfterUtc}: {Error}",
            message.Id, message.Type, message.EventId, message.CorrelationId, message.RetryCount + 1, retryAfterUtc, error);
    }

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
            PaymentSucceededIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentSucceededIntegrationEvent>(message.Payload),
            PaymentFailedIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentFailedIntegrationEvent>(message.Payload),
            PaymentRefundedIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentRefundedIntegrationEvent>(message.Payload),
            PaymentRefundFailedIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentRefundFailedIntegrationEvent>(message.Payload),
            PaymentInitiatedIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentInitiatedIntegrationEvent>(message.Payload),
            PaymentCanceledIntegrationEvent.EventType => JsonSerializer.Deserialize<PaymentCanceledIntegrationEvent>(message.Payload),
            // CreatePaymentRequested/RefundRequested are produced by Orders, not Payments outbox; no need to dispatch here.
            _ => null
        };
}
