using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.5 — Retry, Poison Messages &amp; DLQ Testing. Validates bounded retry, eventual success,
/// poison containment and DLQ diagnostic integrity. Uses in-memory dead-letterer and a simulated
/// consumer retry loop that mirrors IntegrationEventMessageProcessor semantics without requiring
/// a live RabbitMQ broker; SQLite via ReliabilityTestBase provides business-effect isolation.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class RetryPoisonDlqTests : ReliabilityTestBase
{
    private static readonly TimeSpan ZeroDelay = TimeSpan.FromMilliseconds(5);

    // ------------------------------------------------ Transient — bounded retry → eventual success

    [Fact]
    public async Task Transient_Retries_Then_Succeeds_BusinessEffectOne_NoDuplicate_NoDLQ()
    {
        var eventId = Guid.NewGuid();
        const string handler = "TransientHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var sku = new InventorySku($"INV-TR-{Guid.NewGuid():N}"[..8].ToUpperInvariant());
        var dlq = new InMemoryDeadLetterer();
        var retryOptions = new ConsumerRetryOptions
        {
            MaxAttempts = 3,
            BackoffType = ConsumerBackoffType.Fixed,
            InitialDelay = ZeroDelay,
            DelayMultiplier = 1,
            MaxDelay = TimeSpan.FromMilliseconds(50)
        };
        var classifier = CreateClassifier(retryOptions, []);

        var attempts = 0;
        var result = await SimulateRetryLoopAsync(
            retryOptions,
            classifier,
            dlq,
            eventId,
            "inventory.test.transient",
            handler,
            correlationId: Guid.NewGuid(),
            causationId: Guid.NewGuid(),
            async attempt =>
            {
                attempts = attempt;
                using var scope = Services.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
                // First 2 attempts: transient DB lock timeout (retryable). 3rd: success.
                if (attempt <= 2)
                    throw new TimeoutException($"Simulated transient DB lock on attempt {attempt}");
                return await processor.TryProcessAsync(message, async () =>
                {
                    ctx.InventoryItems.Add(InventoryItem.Create(sku, 5, DateTimeOffset.UtcNow));
                    await Task.CompletedTask;
                });
            });

        Assert.Equal(ConsumerMessageAction.Ack, result.Action);
        Assert.Equal(3, attempts);
        Assert.Empty(dlq.Published); // no DLQ on eventual success
        Assert.Equal(1, await InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == sku).CountAsync());
        Assert.Equal(1, await InventoryContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString()).CountAsync());
        // Redelivery after success is duplicate, no second effect.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
            var dup = await processor.TryProcessAsync(message, async () =>
            {
                ctx.InventoryItems.Add(InventoryItem.Create(new InventorySku($"DUP-{Guid.NewGuid():N}"[..6].ToUpperInvariant()), 99, DateTimeOffset.UtcNow));
                await Task.CompletedTask;
            });
            Assert.True(dup.IsDuplicate);
        }
        Assert.Equal(1, await InventoryContext.InventoryItems.AsNoTracking().Where(_ => true).CountAsync());
    }

    [Fact]
    public void Retry_Backoff_Exponential_IsDeterministic_And_Capped()
    {
        var retryOptions = new ConsumerRetryOptions
        {
            BackoffType = ConsumerBackoffType.Exponential,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            DelayMultiplier = 2.0,
            MaxDelay = TimeSpan.FromMilliseconds(250)
        };
        // attempt 1: 100, attempt 2: 200, attempt 3: 400 → capped 250
        Assert.Equal(TimeSpan.FromMilliseconds(100), ConsumerRetryPolicy.GetDelay(retryOptions, 1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), ConsumerRetryPolicy.GetDelay(retryOptions, 2));
        Assert.Equal(TimeSpan.FromMilliseconds(250), ConsumerRetryPolicy.GetDelay(retryOptions, 3));
        Assert.True(ConsumerRetryPolicy.IsExhausted(new ConsumerRetryOptions { MaxAttempts = 3, BackoffType = ConsumerBackoffType.Exponential, InitialDelay = TimeSpan.FromMilliseconds(100), DelayMultiplier = 2.0, MaxDelay = TimeSpan.FromMilliseconds(250) }, 3));
        Assert.False(ConsumerRetryPolicy.IsExhausted(new ConsumerRetryOptions { MaxAttempts = 3, BackoffType = ConsumerBackoffType.Exponential, InitialDelay = TimeSpan.FromMilliseconds(100), DelayMultiplier = 2.0, MaxDelay = TimeSpan.FromMilliseconds(250) }, 2));
    }

    // ------------------------------------------------ Permanent / poison — bounded, no infinite loop

    [Fact]
    public async Task Permanent_Poison_NullReference_DeadLettered_Immediately_NoRetry()
    {
        var eventId = Guid.NewGuid();
        const string handler = "PermanentHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var dlq = new InMemoryDeadLetterer();
        var retryOptions = new ConsumerRetryOptions
        {
            MaxAttempts = 3,
            BackoffType = ConsumerBackoffType.Fixed,
            InitialDelay = ZeroDelay,
            NonRetryableExceptionTypes = [typeof(NullReferenceException)]
        };
        var classifier = CreateClassifier(retryOptions, [typeof(NullReferenceException)]);

        var attempts = 0;
        var result = await SimulateRetryLoopAsync(
            retryOptions,
            classifier,
            dlq,
            eventId,
            "inventory.test.poison",
            handler,
            correlationId: Guid.NewGuid(),
            causationId: Guid.NewGuid(),
            async attempt =>
            {
                attempts = attempt;
                using var scope = Services.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
                return await processor.TryProcessAsync(message, async () =>
                {
                    // Poison: unrecoverable validation failure.
                    throw new NullReferenceException("Simulated poison NullReference");
                });
            });

        // Permanent → dead-lettered on first attempt, no retries.
        Assert.Equal(ConsumerMessageAction.Ack, result.Action); // enriched DLQ copy confirmed → original ACKed
        Assert.Equal(1, attempts);
        Assert.Single(dlq.Published);
        var entry = dlq.Published[0];
        Assert.Equal("processing-failure-permanent", entry.Headers["x-dead-letter-reason"]?.ToString());
        Assert.Equal(1, entry.Headers["x-attempt"]);
        Assert.Equal("NullReferenceException", entry.Headers["x-error-type"]?.ToString());
        Assert.Contains("poison", entry.Headers["x-error-message"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        // No business effect, no infinite loop.
        Assert.Equal(0, await InventoryContext.InventoryItems.AsNoTracking().Where(_ => true).CountAsync());
        Assert.Equal(0, await InventoryContext.ProcessedMessages.AsNoTracking().Where(_ => true).CountAsync());
    }

    [Fact]
    public async Task Poison_Exhausts_MaxAttempts_Then_DeadLettered_NoInfiniteLoop()
    {
        var eventId = Guid.NewGuid();
        const string handler = "ExhaustHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var dlq = new InMemoryDeadLetterer();
        var retryOptions = new ConsumerRetryOptions
        {
            MaxAttempts = 3,
            BackoffType = ConsumerBackoffType.Fixed,
            InitialDelay = ZeroDelay,
            NonRetryableExceptionTypes = []
        };
        var classifier = CreateClassifier(retryOptions, []);

        var attempts = 0;
        var result = await SimulateRetryLoopAsync(
            retryOptions,
            classifier,
            dlq,
            eventId,
            "inventory.test.exhaust",
            handler,
            correlationId: Guid.NewGuid(),
            causationId: Guid.NewGuid(),
            async attempt =>
            {
                attempts = attempt;
                using var scope = Services.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
                return await processor.TryProcessAsync(message, async () =>
                {
                    throw new InvalidOperationException($"Always failing attempt {attempt}");
                });
            });

        // Retryable, exhausted after MaxAttempts → dead-lettered with exhausted reason.
        Assert.Equal(ConsumerMessageAction.Ack, result.Action);
        Assert.Equal(3, attempts);
        Assert.Single(dlq.Published);
        var entry = dlq.Published[0];
        Assert.Equal("processing-failure-exhausted", entry.Headers["x-dead-letter-reason"]?.ToString());
        Assert.Equal(3, entry.Headers["x-attempt"]);
        Assert.Equal("InvalidOperationException", entry.Headers["x-error-type"]?.ToString());
        // No business effect, no infinite loop.
        Assert.Equal(0, await InventoryContext.InventoryItems.AsNoTracking().Where(_ => true).CountAsync());
        Assert.Equal(0, await InventoryContext.ProcessedMessages.AsNoTracking().Where(_ => true).CountAsync());
    }

    // ------------------------------------------------ DLQ metadata & diagnostic integrity

    [Fact]
    public async Task DLQ_Metadata_Contains_Full_Diagnostic_Context_On_Exhausted()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        const string eventType = "inventory.test.dlqmeta";
        const string handler = "DlqMetaHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var dlq = new InMemoryDeadLetterer();
        var retryOptions = new ConsumerRetryOptions { MaxAttempts = 2, BackoffType = ConsumerBackoffType.Fixed, InitialDelay = ZeroDelay };
        var classifier = CreateClassifier(retryOptions, []);

        var body = JsonSerializer.SerializeToUtf8Bytes(new { sku = "DLQ-TEST", qty = 1 });

        var result = await SimulateRetryLoopWithBodyAsync(
            retryOptions,
            classifier,
            dlq,
            eventId,
            eventType,
            handler,
            correlationId,
            causationId,
            EventVersion.V1,
            body,
            async attempt =>
            {
                using var scope = Services.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
                return await processor.TryProcessAsync(message, async () => throw new TimeoutException("Transient for DLQ meta"));
            });

        Assert.Equal(ConsumerMessageAction.Ack, result.Action);
        Assert.Single(dlq.Published);
        var entry = dlq.Published[0];

        // Original event preserved.
        Assert.Equal(eventId.ToString(), entry.OriginalHeaders["EventId"]?.ToString());
        Assert.Equal(eventType, entry.OriginalHeaders["EventType"]?.ToString());
        Assert.Equal(eventId.ToString(), entry.Properties.MessageId);
        Assert.Equal(eventType, entry.Properties.Type);
        Assert.Equal(correlationId.ToString(), entry.Properties.CorrelationId);
        // Body preserved byte-for-byte.
        Assert.Equal(body, entry.Body.ToArray());
        // Attempt, error, timestamp, correlation/causation, consumer.
        Assert.Equal(2, entry.Headers["x-attempt"]);
        Assert.Equal("TimeoutException", entry.Headers["x-error-type"]?.ToString());
        Assert.Contains("Transient", entry.Headers["x-error-message"]?.ToString() ?? "");
        Assert.NotNull(entry.Headers["x-dead-lettered-at"]);
        Assert.True(DateTimeOffset.TryParse(entry.Headers["x-dead-lettered-at"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        Assert.Equal(handler, entry.Headers["x-consumer"]?.ToString());
        Assert.Equal("processing-failure-exhausted", entry.Headers["x-dead-letter-reason"]?.ToString());
        // Correlation/Causation preserved from envelope.
        Assert.Equal(correlationId.ToString(), entry.OriginalHeaders["CorrelationId"]?.ToString());
        Assert.Equal(causationId.ToString(), entry.OriginalHeaders["CausationId"]?.ToString());
        Assert.Equal(EventVersion.V1.ToString(), entry.OriginalHeaders["EventVersion"]?.ToString());
    }

    [Fact]
    public async Task DLQ_Metadata_Contains_Full_Diagnostic_Context_On_Permanent()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        const string eventType = "inventory.test.dlqperm";
        const string handler = "DlqPermHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var dlq = new InMemoryDeadLetterer();
        var retryOptions = new ConsumerRetryOptions
        {
            MaxAttempts = 3,
            BackoffType = ConsumerBackoffType.Fixed,
            InitialDelay = ZeroDelay,
            NonRetryableExceptionTypes = [typeof(ArgumentException)]
        };
        var classifier = CreateClassifier(retryOptions, [typeof(ArgumentException)]);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { invalid = "schema" });

        var result = await SimulateRetryLoopWithBodyAsync(
            retryOptions,
            classifier,
            dlq,
            eventId,
            eventType,
            handler,
            correlationId,
            causationId,
            EventVersion.V1,
            body,
            async attempt =>
            {
                using var scope = Services.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
                return await processor.TryProcessAsync(message, async () => throw new ArgumentException("Invalid schema poison"));
            });

        Assert.Equal(ConsumerMessageAction.Ack, result.Action);
        Assert.Single(dlq.Published);
        var entry = dlq.Published[0];
        Assert.Equal(eventId.ToString(), entry.OriginalHeaders["EventId"]?.ToString());
        Assert.Equal("processing-failure-permanent", entry.Headers["x-dead-letter-reason"]?.ToString());
        Assert.Equal(1, entry.Headers["x-attempt"]); // permanent → no retry, attempt 1
        Assert.Equal("ArgumentException", entry.Headers["x-error-type"]?.ToString());
        Assert.Contains("Invalid schema", entry.Headers["x-error-message"]?.ToString() ?? "");
        Assert.Equal(body, entry.Body.ToArray());
    }

    // ------------------------------------------------ helpers

    private static IIntegrationEventFailureClassifier CreateClassifier(ConsumerRetryOptions options, Type[] nonRetryable)
    {
        options.NonRetryableExceptionTypes = [.. nonRetryable];
        var consumerOptions = Options.Create(new RabbitMQConsumerOptions
        {
            ExchangeName = "test.exchange",
            QueueName = "test.queue",
            Retry = options
        });
        return new IntegrationEventFailureClassifier(consumerOptions);
    }

    private async Task<SimulatedResult> SimulateRetryLoopAsync(
        ConsumerRetryOptions retryOptions,
        IIntegrationEventFailureClassifier classifier,
        InMemoryDeadLetterer dlq,
        Guid eventId,
        string eventType,
        string handler,
        Guid correlationId,
        Guid causationId,
        Func<int, Task<IdempotencyResult>> attemptFunc)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { eventId, eventType });
        return await SimulateRetryLoopWithBodyAsync(retryOptions, classifier, dlq, eventId, eventType, handler, correlationId, causationId, EventVersion.V1, body, attemptFunc);
    }

    private async Task<SimulatedResult> SimulateRetryLoopWithBodyAsync(
        ConsumerRetryOptions retryOptions,
        IIntegrationEventFailureClassifier classifier,
        InMemoryDeadLetterer dlq,
        Guid eventId,
        string eventType,
        string handler,
        Guid correlationId,
        Guid causationId,
        EventVersion version,
        byte[] body,
        Func<int, Task<IdempotencyResult>> attemptFunc)
    {
        var attempt = 1;
        Exception? lastEx = null;
        while (true)
        {
            try
            {
                var result = await attemptFunc(attempt);
                // Success or duplicate → ACK, no DLQ.
                return new SimulatedResult(ConsumerMessageAction.Ack, attempt, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastEx = ex;
                var classification = classifier.Classify(ex);
                if (classification == ConsumerFailureClassification.Permanent)
                {
                    await PublishToDlqAsync(dlq, eventId, eventType, version, correlationId, causationId, handler, attempt, ex, body, IntegrationEventMessageProcessor.PermanentFailureReason);
                    return new SimulatedResult(ConsumerMessageAction.Ack, attempt, ex);
                }

                if (ConsumerRetryPolicy.IsExhausted(retryOptions, attempt))
                {
                    await PublishToDlqAsync(dlq, eventId, eventType, version, correlationId, causationId, handler, attempt, ex, body, IntegrationEventMessageProcessor.ExhaustedReason);
                    return new SimulatedResult(ConsumerMessageAction.Ack, attempt, ex);
                }

                var delay = ConsumerRetryPolicy.GetDelay(retryOptions, attempt);
                await Task.Delay(delay);
                attempt++;
            }
        }
    }

    private static async Task PublishToDlqAsync(
        InMemoryDeadLetterer dlq,
        Guid eventId,
        string eventType,
        EventVersion version,
        Guid correlationId,
        Guid causationId,
        string handler,
        int attempt,
        Exception ex,
        byte[] body,
        string reason)
    {
        var originalHeaders = new Dictionary<string, object?>
        {
            ["EventId"] = eventId.ToString(),
            ["EventType"] = eventType,
            ["EventVersion"] = version.ToString(),
            ["CorrelationId"] = correlationId.ToString(),
            ["CausationId"] = causationId.ToString(),
            ["OccurredAtUtc"] = DateTime.UtcNow.ToString("O")
        };
        var additionalHeaders = new Dictionary<string, object?>
        {
            ["x-dead-letter-reason"] = reason,
            ["x-dead-lettered-at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["x-consumer"] = handler,
            ["x-attempt"] = attempt,
            ["x-error-type"] = ex.GetType().Name,
            ["x-error-message"] = ex.Message
        };
        var properties = new BasicProperties
        {
            MessageId = eventId.ToString(),
            Type = eventType,
            CorrelationId = correlationId.ToString(),
            Headers = originalHeaders
        };
        await dlq.PublishAsync(properties, body, eventType, additionalHeaders);
    }

    private sealed record SimulatedResult(ConsumerMessageAction Action, int Attempts, Exception? LastException);

    private sealed class InMemoryDeadLetterer : IIntegrationEventDeadLetterer
    {
        public List<DeadLetterEntry> Published { get; } = [];
        public Task<bool> PublishAsync(IReadOnlyBasicProperties originalProperties, ReadOnlyMemory<byte> body, string routingKey, IReadOnlyDictionary<string, object?> additionalHeaders, CancellationToken cancellationToken = default)
        {
            var headers = new Dictionary<string, object?>();
            if (originalProperties.Headers is not null)
                foreach (var kv in originalProperties.Headers) headers[kv.Key] = kv.Value;
            foreach (var kv in additionalHeaders) headers[kv.Key] = kv.Value;

            Published.Add(new DeadLetterEntry(
                new Dictionary<string, object?>(originalProperties.Headers ?? new Dictionary<string, object?>()),
                new Dictionary<string, object?>(headers),
                new BasicProperties
                {
                    MessageId = originalProperties.MessageId,
                    Type = originalProperties.Type,
                    CorrelationId = originalProperties.CorrelationId,
                    Headers = new Dictionary<string, object?>(headers)
                },
                body.ToArray()));
            return Task.FromResult(true);
        }
    }

    private sealed record DeadLetterEntry(
        IReadOnlyDictionary<string, object?> OriginalHeaders,
        IReadOnlyDictionary<string, object?> Headers,
        IReadOnlyBasicProperties Properties,
        byte[] Body);
}
