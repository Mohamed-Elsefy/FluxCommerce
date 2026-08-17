using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7.5/7.6/7.7: RabbitMQConsumerBackgroundService against the real broker. Verifies the consumer
/// infrastructure: exchange/queue/dead-letter topology and per-delivery DI scopes, delivery →
/// deserialize → validate → idempotent-handler-invocation → ACK, unknown event types skipped (ADR-008),
/// malformed deliveries rejected to the dead-letter queue (verbatim, broker-added x-death only), Phase
/// 7.6 in-process retry with backoff for retryable business failures (succeeding retries ack,
/// exhaustion dead-letters, non-retryable failures dead-letter immediately), Phase 7.7 enriched
/// terminal dead-lettering (the DLQ copy preserves the original body and headers and carries
/// x-dead-letter-reason/x-attempt/x-error-type/x-error-message/x-consumer metadata), x-death-seeded
/// attempt accounting across dead-letter hops, poison-message isolation, duplicate deliveries
/// acknowledged with exactly one business effect, bounded concurrency via a configurable channel
/// count, and graceful shutdown that stops consuming before the host stops.
/// </summary>
[Collection(RabbitMQConsumerTestCollection.Name)]
public class RabbitMQConsumerIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly RabbitMQConsumerTestFixture _fixture;

    public RabbitMQConsumerIntegrationTests(RabbitMQConsumerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Consumer_DeclaresTopologyAndRoutesBoundEventType()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            var @event = CreateOrderCreated();

            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 1);
            Assert.Equal(1u, await ConsumerCountAsync(provider, queueName));
            await WaitForProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await channel.ExchangeDeclarePassiveAsync(RabbitMQConsumerTestFixture.TestExchangeName, CancellationToken.None);
            await channel.QueueDeclarePassiveAsync(queueName, CancellationToken.None);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_InvokesHandlerWithEventAndEnvelope_AndAcknowledges()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            var @event = CreateOrderCreated();

            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 1);
            var invocation = recorder.Invocations.Single();

            Assert.Equal(RecordingOrderCreatedHandler.StableHandlerName, invocation.HandlerName);
            Assert.Equal(@event, invocation.Event);
            Assert.Equal(@event.EventId, invocation.Envelope.EventId);
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, invocation.Envelope.EventType);
            Assert.Equal(EventVersion.V1, invocation.Envelope.EventVersion);
            Assert.Equal(@event.CorrelationId, invocation.Envelope.CorrelationId);
            Assert.Equal(@event.OccurredAtUtc, invocation.Envelope.OccurredAtUtc);

            await WaitForProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_SkipsUnregisteredEventType_AndAcknowledges()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: RabbitMQConsumerTestFixture.TestExchangeName,
                routingKey: OrderCancelledIntegrationEvent.EventType,
                arguments: null!,
                noWait: false,
                cancellationToken: default);

            var cancelled = new OrderCancelledIntegrationEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ORD-IT-CANCEL",
                new DateTimeOffset(2026, 8, 14, 18, 0, 0, TimeSpan.Zero));
            await PublishAsync(provider, OrderCancelledIntegrationEvent.EventType, cancelled);

            await WaitForQueueEmptyAsync(provider, queueName);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(0, recorder.InvocationCount);
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_RejectsMalformedPayload_ToDeadLetterQueue()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            var @event = CreateOrderCreated();

            var properties = BuildRawProperties(
                @event.EventId.ToString(),
                OrderCreatedIntegrationEvent.EventType,
                EventVersion.V1.ToString(),
                @event.CorrelationId.ToString(),
                @event.OccurredAtUtc.ToString("O"));
            await PublishRawAsync(provider, OrderCreatedIntegrationEvent.EventType, properties, "not-a-json-payload"u8.ToArray());

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(0, recorder.InvocationCount);
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_RejectsInvalidHeaders_ToDeadLetterQueue()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            var @event = CreateOrderCreated();

            var properties = BuildRawProperties(
                eventId: null,
                OrderCreatedIntegrationEvent.EventType,
                EventVersion.V1.ToString(),
                @event.CorrelationId.ToString(),
                @event.OccurredAtUtc.ToString("O"));
            await PublishRawAsync(
                provider,
                OrderCreatedIntegrationEvent.EventType,
                properties,
                JsonSerializer.SerializeToUtf8Bytes(@event));

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(0, recorder.InvocationCount);
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_MalformedDelivery_DeadLettersVerbatim_WithoutEnrichment()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            var @event = CreateOrderCreated();

            var properties = BuildRawProperties(
                @event.EventId.ToString(),
                OrderCreatedIntegrationEvent.EventType,
                EventVersion.V1.ToString(),
                @event.CorrelationId.ToString(),
                @event.OccurredAtUtc.ToString("O"));
            await PublishRawAsync(provider, OrderCreatedIntegrationEvent.EventType, properties, "not-a-json-payload"u8.ToArray());

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(0, recorder.InvocationCount);
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal("not-a-json-payload", Encoding.UTF8.GetString(deadLettered.Body));

            Assert.True(deadLettered.Headers.ContainsKey("x-death"), "The broker adds x-death when dead-lettering the rejected delivery.");
            Assert.Equal(@event.EventId.ToString(), HeaderString(deadLettered.Headers["EventId"]));
            Assert.DoesNotContain(IntegrationEventMessageProcessor.DeadLetterReasonHeader, deadLettered.Headers.Keys);
            Assert.DoesNotContain(IntegrationEventMessageProcessor.DeadLetterAttemptHeader, deadLettered.Headers.Keys);
            Assert.DoesNotContain(IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader, deadLettered.Headers.Keys);
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_RedeliveredAfterDeadLetterHop_SeedsAttemptsFromXDeath()
    {
        var recorder = new TestConsumerRecorder { FailuresRemaining = int.MaxValue };
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options =>
            {
                options.Retry.MaxAttempts = 3;
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(50);
            });

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            var properties = BuildRawProperties(
                @event.EventId.ToString(),
                OrderCreatedIntegrationEvent.EventType,
                EventVersion.V1.ToString(),
                @event.CorrelationId.ToString(),
                @event.OccurredAtUtc.ToString("O"));
            properties.Headers!["x-death"] = new List<object>
            {
                new Dictionary<string, object> { ["count"] = 2L }
            };
            await PublishRawAsync(
                provider,
                OrderCreatedIntegrationEvent.EventType,
                properties,
                JsonSerializer.SerializeToUtf8Bytes(@event));

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);

            // Two prior broker-level dead-letter hops seed the attempt counter, so the single in-process
            // failure is already terminal (attempt 3 of MaxAttempts 3) and never retried in-process.
            Assert.Equal(1, recorder.InvocationCount);
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal(
                "processing-failure-exhausted",
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.Equal(
                3,
                Convert.ToInt32(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_PoisonMessage_DoesNotBlockSubsequentDeliveries()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            var malformed = CreateOrderCreated();
            var properties = BuildRawProperties(
                malformed.EventId.ToString(),
                OrderCreatedIntegrationEvent.EventType,
                EventVersion.V1.ToString(),
                malformed.CorrelationId.ToString(),
                malformed.OccurredAtUtc.ToString("O"));
            await PublishRawAsync(provider, OrderCreatedIntegrationEvent.EventType, properties, "poison"u8.ToArray());
            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 1);
            await WaitForProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, recorder.InvocationCount);
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_DeadLetterTopology_RoutesEnrichedCopiesToDeadLetterQueue()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await channel.ExchangeDeclarePassiveAsync(DeadLetterExchangeName(queueName), CancellationToken.None);
            await channel.QueueDeclarePassiveAsync(DeadLetterQueueName(queueName), CancellationToken.None);

            // The DLQ binds to the DLX with routing key "#", so any publish to the DLX is recoverable.
            await channel.BasicPublishAsync(
                exchange: DeadLetterExchangeName(queueName),
                routingKey: "any.routing.key",
                mandatory: false,
                basicProperties: new BasicProperties(),
                body: "topology-probe"u8.ToArray(),
                cancellationToken: default);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            var probe = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal("topology-probe", Encoding.UTF8.GetString(probe.Body));
        }
        finally
        {
            await DeleteQueueAsync(provider, queueName);
        }
    }

    [Fact]
    public async Task Consumer_RetryableFailure_RetriesInProcess_ThenAcknowledges()
    {
        var recorder = new TestConsumerRecorder { FailuresRemaining = 1 };
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options => options.Retry.InitialDelay = TimeSpan.FromMilliseconds(50));

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 2);
            await WaitForProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(2, recorder.InvocationCount);
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName));

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_RetryExhaustion_DeadLettersMessage_WithEnrichedMetadata()
    {
        var recorder = new TestConsumerRecorder { FailuresRemaining = int.MaxValue };
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options =>
            {
                options.Retry.MaxAttempts = 3;
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(50);
            });

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(3, recorder.InvocationCount);
            Assert.Equal(0, await CountProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal(
                "processing-failure-exhausted",
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.Equal(
                3,
                Convert.ToInt32(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]));
            Assert.Equal(
                nameof(InvalidOperationException),
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader]));
            Assert.Equal(
                RecordingOrderCreatedHandler.StableHandlerName,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterConsumerHeader]));
            Assert.Equal(
                @event.EventId.ToString(),
                HeaderString(deadLettered.Headers["EventId"]));

            var enrichedEvent = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(deadLettered.Body);
            Assert.NotNull(enrichedEvent);
            Assert.Equal(@event.EventId, enrichedEvent.EventId);
            Assert.Equal(@event.CorrelationId, enrichedEvent.CorrelationId);
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_NonRetryableFailure_DeadLettersImmediately_WithEnrichedMetadata()
    {
        var recorder = new TestConsumerRecorder { FailuresRemaining = int.MaxValue };
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options => options.Retry.NonRetryableExceptionTypes.Add(typeof(InvalidOperationException)));

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);

            await consumer.StopAsync(CancellationToken.None);
            Assert.Equal(1, recorder.InvocationCount);
            Assert.Equal(0, await CountProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, queueName));
            Assert.Equal(1u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal(
                "processing-failure-permanent",
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.Equal(
                1,
                Convert.ToInt32(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]));
            Assert.Equal(
                nameof(InvalidOperationException),
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader]));
            Assert.Equal(
                RecordingOrderCreatedHandler.StableHandlerName,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterConsumerHeader]));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_RetryBackoff_AppliesConfiguredDelayBetweenAttempts()
    {
        var recorder = new TestConsumerRecorder { FailuresRemaining = 1 };
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options =>
            {
                options.Retry.MaxAttempts = 3;
                options.Retry.BackoffType = ConsumerBackoffType.Fixed;
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(500);
            });

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 2);
            await WaitForProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(2, recorder.InvocationCount);
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, RecordingOrderCreatedHandler.StableHandlerName));

            var invocations = recorder.Invocations;
            var gap = invocations[1].TimestampUtc - invocations[0].TimestampUtc;
            Assert.True(
                gap >= TimeSpan.FromMilliseconds(400),
                $"Expected at least a 400ms backoff gap between retry attempts, observed {gap}.");
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], []);
        }
    }

    [Fact]
    public async Task Consumer_DuplicateDelivery_ProducesSingleBusinessEffect()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated();
        var sku = EffectSku(@event.EventId);
        await using var provider = _fixture.BuildConsumerProvider<InventoryEffectOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 1);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, recorder.InvocationCount);
            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryEffectOrderCreatedHandler.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [@event.EventId], [sku]);
        }
    }

    [Fact]
    public async Task Consumer_WithConcurrencyTwo_ConsumesThroughTwoChannels()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        var events = Enumerable.Range(0, 6).Select(_ => CreateOrderCreated()).ToList();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(
            queueName,
            recorder,
            options => options.Concurrency = 2);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);
            await WaitForConsumerCountAsync(provider, queueName, 2);

            Assert.Equal(2u, await ConsumerCountAsync(provider, queueName));

            foreach (var @event in events)
                await PublishAsync(provider, @event);

            await WaitForInvocationsAsync(recorder, 6);
            await WaitForProcessedCountAsync(events.Select(e => e.EventId), RecordingOrderCreatedHandler.StableHandlerName, 6);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(6, recorder.InvocationCount);
            Assert.Equal(6, await CountProcessedAsync(events.Select(e => e.EventId), RecordingOrderCreatedHandler.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, events.Select(e => e.EventId), []);
        }
    }

    [Fact]
    public async Task Consumer_GracefulShutdown_StopsConsumingWithoutLoss()
    {
        var recorder = new TestConsumerRecorder();
        var queueName = UniqueQueueName();
        var first = CreateOrderCreated();
        var second = CreateOrderCreated();
        await using var provider = _fixture.BuildConsumerProvider<RecordingOrderCreatedHandler>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, first);
            await PublishAsync(provider, second);

            await WaitForInvocationsAsync(recorder, 2);
            await WaitForProcessedAsync(first.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await WaitForProcessedAsync(second.EventId, RecordingOrderCreatedHandler.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(2, recorder.InvocationCount);
            Assert.Equal(1, await CountProcessedAsync(first.EventId, RecordingOrderCreatedHandler.StableHandlerName));
            Assert.Equal(1, await CountProcessedAsync(second.EventId, RecordingOrderCreatedHandler.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [first.EventId, second.EventId], []);
        }
    }

    private static async Task<RabbitMQConsumerBackgroundService> StartConsumerAsync(ServiceProvider provider)
    {
        var consumer = provider.GetServices<IHostedService>()
            .OfType<RabbitMQConsumerBackgroundService>()
            .Single();
        await consumer.StartAsync(CancellationToken.None);
        return consumer;
    }

    private static Task PublishAsync(IServiceProvider provider, OrderCreatedIntegrationEvent @event) =>
        PublishAsync(provider, OrderCreatedIntegrationEvent.EventType, @event);

    private static async Task PublishAsync(IServiceProvider provider, string eventType, object @event)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(eventType, (IIntegrationEvent)@event);
        Assert.True(confirmed);
    }

    private static async Task PublishRawAsync(
        IServiceProvider provider,
        string routingKey,
        BasicProperties properties,
        byte[] body)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        await channel.BasicPublishAsync(
            exchange: RabbitMQConsumerTestFixture.TestExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: default);
    }

    private static async Task WaitForConsumerAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ConsumerCountAsync(provider, queueName) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No consumer connected to queue '{queueName}' within {Timeout}.");
    }

    private static async Task WaitForConsumerCountAsync(IServiceProvider provider, string queueName, uint expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ConsumerCountAsync(provider, queueName) >= expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Expected {expected} consumers connected to queue '{queueName}' within {Timeout}.");
    }

    private static async Task WaitForInvocationsAsync(TestConsumerRecorder recorder, int expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (recorder.InvocationCount >= expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Expected at least {expected} handler invocations, observed {recorder.InvocationCount}.");
    }

    private async Task WaitForProcessedAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountProcessedAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' within {Timeout}.");
    }

    private async Task WaitForProcessedCountAsync(IEnumerable<Guid> eventIds, string handler, int expected)
    {
        var keys = eventIds.Select(id => id.ToString()).ToList();
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var count = await context.ProcessedMessages.CountAsync(
                message => keys.Contains(message.Key) && message.Handler == handler);
            if (count >= expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Expected at least {expected} processed messages for '{handler}' within {Timeout}.");
    }

    private static Task AckSettleAsync() => Task.Delay(300);

    private static async Task WaitForQueueEmptyAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GetMessageCountAsync(provider, queueName) == 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Queue '{queueName}' did not drain within {Timeout}.");
    }

    private static async Task WaitForMessageCountAsync(IServiceProvider provider, string queueName, uint expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GetMessageCountAsync(provider, queueName) == expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Queue '{queueName}' did not reach {expected} message(s) within {Timeout}.");
    }

    private static async Task<uint> GetMessageCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            var result = await channel.QueueDeclarePassiveAsync(queueName, CancellationToken.None);
            return result.MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    // RabbitMQ.Client 7.x decodes string header values as byte[]; normalize the same way the consumer
    // processor's ReadHeader does so assertions compare the decoded text.
    private static string HeaderString(object? value) => value switch
    {
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        null => string.Empty,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static async Task<(byte[] Body, IDictionary<string, object?> Headers)> GetSingleDeadLetteredMessageAsync(
        IServiceProvider provider,
        string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        var result = await channel.BasicGetAsync(
            queue: DeadLetterQueueName(queueName),
            autoAck: true,
            cancellationToken: default);
        Assert.NotNull(result);
        return (result.Body.ToArray(), result.BasicProperties.Headers!);
    }

    private static async Task<uint> ConsumerCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            return await channel.ConsumerCountAsync(queueName, CancellationToken.None);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static Task DeleteQueueAsync(IServiceProvider provider, string queueName) =>
        Task.Run(async () =>
        {
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await TryDeleteQueueAsync(channel, DeadLetterQueueName(queueName));
            await TryDeleteQueueAsync(channel, queueName);
            try
            {
                await channel.ExchangeDeleteAsync(
                    exchange: DeadLetterExchangeName(queueName),
                    ifUnused: false,
                    noWait: false,
                    cancellationToken: default);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
            }
        });

    private static async Task TryDeleteQueueAsync(IChannel channel, string queueName)
    {
        try
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
        }
    }

    private async Task<int> CountProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<int> CountProcessedAsync(IEnumerable<Guid> eventIds, string handler)
    {
        var keys = eventIds.Select(id => id.ToString()).ToList();
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => keys.Contains(message.Key) && message.Handler == handler);
    }

    private async Task<int> CountItemsAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems.CountAsync(item => item.Id == new InventorySku(sku));
    }

    private async Task CleanupAsync(IServiceProvider provider, string queueName, IEnumerable<Guid> eventIds, IEnumerable<string> skus)
    {
        var keys = eventIds.Select(id => id.ToString()).ToList();
        var itemSkus = skus.Select(sku => new InventorySku(sku)).ToList();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            if (keys.Count > 0)
                await context.ProcessedMessages.Where(message => keys.Contains(message.Key)).ExecuteDeleteAsync();
            if (itemSkus.Count > 0)
                await context.InventoryItems.Where(item => itemSkus.Contains(item.Id)).ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"ORD-IT-{Guid.NewGuid():N}"[..16],
            new DateTimeOffset(2026, 8, 14, 19, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = Guid.NewGuid()
        };

    private static BasicProperties BuildRawProperties(
        string? eventId,
        string? eventType,
        string? eventVersion,
        string? correlationId,
        string? occurredAtUtc)
    {
        var headers = new Dictionary<string, object?>();
        if (eventId is not null) headers["EventId"] = eventId;
        if (eventType is not null) headers["EventType"] = eventType;
        if (eventVersion is not null) headers["EventVersion"] = eventVersion;
        if (correlationId is not null) headers["CorrelationId"] = correlationId;
        if (occurredAtUtc is not null) headers["OccurredAtUtc"] = occurredAtUtc;

        return new BasicProperties
        {
            Type = eventType,
            MessageId = eventId,
            Headers = headers.Count > 0 ? headers : null
        };
    }

    internal static string EffectSku(Guid eventId) =>
        $"CONSUMER-{eventId:N}"[..Math.Min("CONSUMER-".Length + 32, 63)];
}

/// <summary>
/// Thread-safe recorder shared by the consumer handlers so assertions can observe handler
/// invocations and their inputs regardless of which consumer channel processed the delivery.
/// </summary>
public sealed class TestConsumerRecorder
{
    private readonly object _gate = new();
    private readonly List<TestConsumerInvocation> _invocations = [];

    public int FailuresRemaining { get; set; }

    public int InvocationCount
    {
        get
        {
            lock (_gate)
            {
                return _invocations.Count;
            }
        }
    }

    public IReadOnlyList<TestConsumerInvocation> Invocations
    {
        get
        {
            lock (_gate)
            {
                return _invocations.ToList();
            }
        }
    }

    public void Record(string handlerName, OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope)
    {
        lock (_gate)
        {
            _invocations.Add(new TestConsumerInvocation(handlerName, @event, envelope, DateTimeOffset.UtcNow));
        }
    }

    public bool TryFail()
    {
        lock (_gate)
        {
            if (FailuresRemaining <= 0)
                return false;

            FailuresRemaining--;
            return true;
        }
    }
}

public sealed record TestConsumerInvocation(
    string HandlerName,
    OrderCreatedIntegrationEvent Event,
    IntegrationEventEnvelope Envelope,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Phase 7.5/7.6 test handler: records every invocation with its timestamp and can simulate a
/// configured number of business failures to exercise the in-process retry path.
/// </summary>
public sealed class RecordingOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public const string StableHandlerName = "IntegrationTests.ProjectOrderSummary";

    private readonly TestConsumerRecorder _recorder;

    public RecordingOrderCreatedHandler(TestConsumerRecorder recorder)
    {
        _recorder = recorder;
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _recorder.Record(StableHandlerName, @event, envelope);

        return _recorder.TryFail()
            ? Task.FromException(new InvalidOperationException("Simulated business processing failure."))
            : Task.CompletedTask;
    }
}

/// <summary>
/// Phase 7.5 test handler that produces a real business effect (a persisted inventory item derived
/// from the event id) so idempotent duplicate handling is observable through the database.
/// </summary>
public sealed class InventoryEffectOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public const string StableHandlerName = "IntegrationTests.InventoryEffect";

    private readonly TestConsumerRecorder _recorder;
    private readonly InventoryDbContext _context;

    public InventoryEffectOrderCreatedHandler(TestConsumerRecorder recorder, InventoryDbContext context)
    {
        _recorder = recorder;
        _context = context;
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _recorder.Record(StableHandlerName, @event, envelope);
        _context.InventoryItems.Add(InventoryItem.Create(
            new InventorySku(RabbitMQConsumerIntegrationTests.EffectSku(envelope.EventId)),
            10,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }
}