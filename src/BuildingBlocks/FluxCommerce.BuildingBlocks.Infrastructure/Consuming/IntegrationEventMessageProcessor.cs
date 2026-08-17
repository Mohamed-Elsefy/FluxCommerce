using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Phase 7.5/7.6/7.7 message processor: deserialize → validate → resolve handler → invoke through the
/// idempotency boundary → apply the retry policy → decide the transport action.
///
/// <para>
/// The wire <c>EventType</c> is read from the <c>EventType</c> transport header (falling back to the
/// AMQP <c>Type</c> property) and resolved against the consumer registry. An unknown event type is
/// ACKed and skipped (ADR-008) — this consumer does not handle it, and it is not a poison message for
/// this queue. A message that cannot be resolved into a valid contract (missing/invalid metadata
/// headers, corrupt payload, or a payload whose metadata disagrees with the transport envelope) is a
/// permanent failure and is rejected without requeue (the broker dead-letters it verbatim through the
/// queue's <c>x-dead-letter-exchange</c>). The envelope is built from the transport headers (the
/// authoritative metadata, matching the Phase 6 publisher), and the deserialized event's own
/// identity/version must agree with it.
/// </para>
///
/// <para>
/// Processing is delegated to <see cref="IIdempotentProcessor"/> with
/// <c>ProcessedMessage.Create(envelope.EventId, handler.HandlerName)</c> (ADR-008): the handler runs
/// inside the module's atomic transaction, so a business failure rolls everything back (no
/// <c>ProcessedMessage</c> is created) and the attempt is retryable, while a duplicate is acknowledged
/// without re-execution. A failure is classified by
/// <see cref="IIntegrationEventFailureClassifier"/>: permanent failures dead-letter immediately;
/// retryable failures are retried in-process with backoff (see <see cref="ConsumerRetryPolicy"/> and
/// <see cref="ConsumerRetryOptions"/>) up to <c>MaxAttempts</c> total attempts, then dead-lettered. The
/// attempt counter is seeded from the broker <c>x-death</c> redelivery count (the maximum count across
/// dead-letter entries), so attempts stay bounded even across crashes/dead-letter hops.
/// </para>
///
/// <para>
/// Phase 7.7 terminal-failure handling: a permanent or exhausted failure publishes an enriched copy of
/// the delivery to the dead-letter exchange via <see cref="IIntegrationEventDeadLetterer"/> — the full
/// original body + headers are preserved and enriched with failure metadata (reason, consumer, attempt,
/// error type/message, dead-lettered-at) so the dead-letter queue is a recoverable, diagnosable record.
/// When the enriched copy is confirmed, the original is ACKed; when the publish fails, the processor
/// falls back to <see cref="ConsumerMessageAction.DeadLetter"/> (Nack requeue:false) and the broker's
/// <c>x-dead-letter-exchange</c> preserves the original verbatim — a terminal failure can never silently
/// lose the message or loop forever. Both a fresh processing and a duplicate yield
/// <see cref="ConsumerMessageAction.Ack"/>; malformed/invalid deliveries yield
/// <see cref="ConsumerMessageAction.Reject"/>. Cancellation (shutdown) propagates and leaves the
/// delivery unacknowledged for redelivery.
/// </para>
/// </summary>
public sealed class IntegrationEventMessageProcessor : IIntegrationEventMessageProcessor
{
    private const string EventIdHeader = "EventId";
    private const string EventTypeHeader = "EventType";
    private const string EventVersionHeader = "EventVersion";
    private const string CorrelationIdHeader = "CorrelationId";
    private const string CausationIdHeader = "CausationId";
    private const string OccurredAtUtcHeader = "OccurredAtUtc";
    private const string XDeathHeader = "x-death";
    private const string XDeathCountKey = "count";

    internal const string DeadLetterReasonHeader = "x-dead-letter-reason";
    internal const string DeadLetterAtHeader = "x-dead-lettered-at";
    internal const string DeadLetterConsumerHeader = "x-consumer";
    internal const string DeadLetterAttemptHeader = "x-attempt";
    internal const string DeadLetterErrorTypeHeader = "x-error-type";
    internal const string DeadLetterErrorMessageHeader = "x-error-message";

    internal const string PermanentFailureReason = "processing-failure-permanent";
    internal const string ExhaustedReason = "processing-failure-exhausted";

    private readonly IServiceProvider _serviceProvider;
    private readonly IIntegrationEventConsumerRegistry _registry;
    private readonly IOptions<RabbitMQConsumerOptions> _options;
    private readonly IIntegrationEventFailureClassifier _failureClassifier;
    private readonly IIntegrationEventDeadLetterer _deadLetterer;
    private readonly ILogger<IntegrationEventMessageProcessor> _logger;

    public IntegrationEventMessageProcessor(
        IServiceProvider serviceProvider,
        IIntegrationEventConsumerRegistry registry,
        IOptions<RabbitMQConsumerOptions> options,
        IIntegrationEventFailureClassifier failureClassifier,
        IIntegrationEventDeadLetterer deadLetterer,
        ILogger<IntegrationEventMessageProcessor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _failureClassifier = failureClassifier ?? throw new ArgumentNullException(nameof(failureClassifier));
        _deadLetterer = deadLetterer ?? throw new ArgumentNullException(nameof(deadLetterer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConsumerMessageAction> ProcessAsync(
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var eventType = ReadHeader(properties, EventTypeHeader) ?? properties.Type;

        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("Rejecting a delivery without an event type: the message cannot be routed to a consumer.");
            return ConsumerMessageAction.Reject;
        }

        var descriptor = _registry.Resolve(eventType);

        if (descriptor is null)
        {
            // ADR-008: this consumer does not handle the event type; it is acknowledged and skipped
            // rather than treated as poison (the queue only receives types bound to it).
            _logger.LogInformation("Consumer skipped delivery of unhandled event type {EventType}.", eventType);
            return ConsumerMessageAction.Ack;
        }

        if (!TryBuildEnvelope(properties, eventType, out var envelope, out var metadataError))
        {
            _logger.LogWarning(
                "Consumer rejected malformed {EventType} delivery: {Error}",
                eventType,
                metadataError);
            return ConsumerMessageAction.Reject;
        }

        var integrationEvent = Deserialize(descriptor.EventContractType, body);

        if (integrationEvent is null)
        {
            _logger.LogWarning(
                "Consumer rejected {EventType} delivery {EventId}: the payload could not be deserialized.",
                eventType,
                envelope.EventId);
            return ConsumerMessageAction.Reject;
        }

        if (integrationEvent.EventId != envelope.EventId || integrationEvent.Version != envelope.EventVersion)
        {
            _logger.LogWarning(
                "Consumer rejected {EventType} delivery {EventId}: the payload metadata does not match the transport envelope.",
                eventType,
                envelope.EventId);
            return ConsumerMessageAction.Reject;
        }

        var retryOptions = _options.Value.Retry;
        var attempt = 1 + CountRedeliveries(properties);
        string? handlerName = null;

        while (true)
        {
            // A fresh DI scope per attempt: the scoped DbContext (and the IIdempotentProcessor that
            // owns it) is only safe to use once — after a business failure rolls the transaction back,
            // EF Core considers the context poisoned and reusing it fails before the handler runs.
            using var attemptScope = _serviceProvider.CreateScope();

            try
            {
                var handler = attemptScope.ServiceProvider.GetRequiredService(descriptor.HandlerType);
                handlerName = GetHandlerName(descriptor, handler);
                var processor = attemptScope.ServiceProvider.GetRequiredService<IIdempotentProcessor>();

                var result = await processor
                    .TryProcessAsync(
                        ProcessedMessage.Create(envelope.EventId, handlerName),
                        () => InvokeHandlerAsync(descriptor, handler, integrationEvent, envelope, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsDuplicate)
                {
                    _logger.LogInformation(
                        "Consumer {Handler} acknowledged duplicate {EventType} event {EventId} (attempt {Attempt}; first processed {ProcessedAtUtc:O}).",
                        handlerName,
                        eventType,
                        envelope.EventId,
                        attempt,
                        result.ProcessedAtUtc);
                }
                else
                {
                    _logger.LogInformation(
                        "Consumer {Handler} processed {EventType} event {EventId} on attempt {Attempt} (correlation {CorrelationId}).",
                        handlerName,
                        eventType,
                        envelope.EventId,
                        attempt,
                        envelope.CorrelationId);
                }

                return ConsumerMessageAction.Ack;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown in progress: the delivery is left unacknowledged so the broker redelivers it
                // after the channel closes (at-least-once). Cancelled work never commits and never acks.
                throw;
            }
            catch (Exception exception)
            {
                var classification = _failureClassifier.Classify(exception);

                if (classification == ConsumerFailureClassification.Permanent)
                {
                    _logger.LogError(
                        exception,
                        "Consumer {Handler} dead-lettered {EventType} event {EventId} (attempt {Attempt}): the failure is classified permanent.",
                        descriptor.HandlerType.Name,
                        eventType,
                        envelope.EventId,
                        attempt);
                    return await DeadLetterAsync(
                        properties,
                        body,
                        eventType,
                        PermanentFailureReason,
                        handlerName,
                        attempt,
                        exception,
                        cancellationToken).ConfigureAwait(false);
                }

                if (ConsumerRetryPolicy.IsExhausted(retryOptions, attempt))
                {
                    _logger.LogError(
                        exception,
                        "Consumer {Handler} dead-lettered {EventType} event {EventId} after {Attempt} attempt(s) (correlation {CorrelationId}): the retry policy is exhausted.",
                        descriptor.HandlerType.Name,
                        eventType,
                        envelope.EventId,
                        attempt,
                        envelope.CorrelationId);
                    return await DeadLetterAsync(
                        properties,
                        body,
                        eventType,
                        ExhaustedReason,
                        handlerName,
                        attempt,
                        exception,
                        cancellationToken).ConfigureAwait(false);
                }

                var delay = ConsumerRetryPolicy.GetDelay(retryOptions, attempt);

                _logger.LogWarning(
                    exception,
                    "Consumer {Handler} retryable failure for {EventType} event {EventId} (attempt {Attempt} of {MaxAttempts}, correlation {CorrelationId}); retrying in {Delay}.",
                    descriptor.HandlerType.Name,
                    eventType,
                    envelope.EventId,
                    attempt,
                    retryOptions.MaxAttempts,
                    envelope.CorrelationId,
                    delay);

                // A shutdown that happens while waiting throws here and propagates (unacked → redelivery).
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
    }

    /// <summary>
    /// Terminal-failure routing (Phase 7.7): publishes an enriched copy of the delivery to the dead-letter
    /// exchange — the full original body and headers preserved, plus failure metadata (reason, consumer,
    /// attempt, error type/message, occurred-at) — and returns <see cref="ConsumerMessageAction.Ack"/> so
    /// the original delivery is acknowledged once the enriched copy is confirmed. When the publish cannot
    /// be confirmed, returns <see cref="ConsumerMessageAction.DeadLetter"/> and the consumer host rejects
    /// the original (Nack requeue:false), which the queue's <c>x-dead-letter-exchange</c> routes to the
    /// dead-letter queue verbatim. Either way the terminal failure is preserved and never silently lost.
    /// </summary>
    private async Task<ConsumerMessageAction> DeadLetterAsync(
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        string eventType,
        string reason,
        string? consumer,
        int? attempt,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var additionalHeaders = new Dictionary<string, object?>
        {
            [DeadLetterReasonHeader] = reason,
            [DeadLetterAtHeader] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(consumer))
            additionalHeaders[DeadLetterConsumerHeader] = consumer;

        if (attempt.HasValue)
            additionalHeaders[DeadLetterAttemptHeader] = attempt.Value;

        if (exception is not null)
        {
            additionalHeaders[DeadLetterErrorTypeHeader] = exception.GetType().Name;
            if (!string.IsNullOrWhiteSpace(exception.Message))
                additionalHeaders[DeadLetterErrorMessageHeader] = exception.Message;
        }

        var published = await _deadLetterer
            .PublishAsync(properties, body, eventType, additionalHeaders, cancellationToken)
            .ConfigureAwait(false);

        if (published)
            return ConsumerMessageAction.Ack;

        _logger.LogError(
            "Consumer could not confirm the enriched dead-letter copy for {EventType} ({Reason}); the original delivery will be dead-lettered verbatim by the broker (Nack requeue:false).",
            eventType,
            reason);
        return ConsumerMessageAction.DeadLetter;
    }

    private static int CountRedeliveries(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null
            || !properties.Headers.TryGetValue(XDeathHeader, out var value)
            || value is not IEnumerable entries)
        {
            return 0;
        }

        // RabbitMQ's x-death header holds one entry per dead-letter hop (exchange + queue + reason);
        // each entry's "count" is how many times the message was dead-lettered to that hop. The message
        // lives in exactly one queue at a time, so the total number of prior dead-letter events is the
        // maximum count across entries (summing entries for different hops would over-count a single
        // message). This seeds the attempt counter so attempts stay bounded across dead-letter hops.
        var maxCount = 0;

        foreach (var entry in entries)
        {
            if (entry is not IDictionary table || !table.Contains(XDeathCountKey))
                continue;

            var count = table[XDeathCountKey] switch
            {
                long longCount => (int)longCount,
                int intCount => intCount,
                _ => 0
            };

            maxCount = Math.Max(maxCount, count);
        }

        return maxCount;
    }

    private static IIntegrationEvent? Deserialize(Type contractType, ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize(body.Span, contractType) as IIntegrationEvent;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetHandlerName(IntegrationEventConsumerDescriptor descriptor, object handler)
    {
        var closedInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(descriptor.EventContractType);
        var handlerNameProperty = closedInterface.GetProperty("HandlerName")
            ?? throw new InvalidOperationException($"Handler '{handler.GetType().Name}' does not expose 'HandlerName'.");
        var handlerName = handlerNameProperty.GetValue(handler) as string;

        if (string.IsNullOrWhiteSpace(handlerName))
        {
            throw new InvalidOperationException(
                $"Handler '{handler.GetType().Name}' must return a stable, non-empty 'HandlerName'.");
        }

        return handlerName!;
    }

    private static async Task InvokeHandlerAsync(
        IntegrationEventConsumerDescriptor descriptor,
        object handler,
        IIntegrationEvent integrationEvent,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var closedInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(descriptor.EventContractType);
        var handleMethod = closedInterface.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"Handler '{handler.GetType().Name}' does not expose 'HandleAsync'.");

        var task = (Task)handleMethod.Invoke(handler, [integrationEvent, envelope, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private static bool TryBuildEnvelope(
        IReadOnlyBasicProperties properties,
        string eventType,
        out IntegrationEventEnvelope envelope,
        out string error)
    {
        envelope = null!;
        error = string.Empty;

        if (!Guid.TryParse(ReadHeader(properties, EventIdHeader), out var eventId))
        {
            error = "The EventId header is missing or not a valid GUID.";
            return false;
        }

        if (!EventVersion.TryParse(ReadHeader(properties, EventVersionHeader), out var eventVersion))
        {
            error = "The EventVersion header is missing or malformed.";
            return false;
        }

        var correlationId = Guid.TryParse(ReadHeader(properties, CorrelationIdHeader), out var parsedCorrelation)
            ? parsedCorrelation
            : Guid.Empty;

        var causationId = Guid.TryParse(ReadHeader(properties, CausationIdHeader), out var parsedCausation)
            ? parsedCausation
            : (Guid?)null;

        if (!DateTime.TryParse(
                ReadHeader(properties, OccurredAtUtcHeader),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var occurredAtUtc))
        {
            error = "The OccurredAtUtc header is missing or malformed.";
            return false;
        }

        envelope = new IntegrationEventEnvelope
        {
            EventId = eventId,
            EventType = eventType,
            EventVersion = eventVersion,
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = occurredAtUtc
        };

        return true;
    }

    private static string? ReadHeader(IReadOnlyBasicProperties properties, string name)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(name, out var value) || value is null)
            return null;

        return value switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value.ToString()
        };
    }
}