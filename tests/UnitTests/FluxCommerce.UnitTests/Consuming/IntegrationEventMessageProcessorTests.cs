using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 7.5/7.6/7.7: the message processor turns a single RabbitMQ delivery into a consumer processing
/// action — resolve the contract by event type, build the envelope from the transport headers with
/// validation, deserialize and verify the payload, then invoke the registered handler through the
/// canonical <c>IIdempotentProcessor</c> boundary (idempotency identity = EventId + HandlerName).
/// Unknown event types are acknowledged and skipped; malformed deliveries are rejected (broker
/// dead-letter). Phase 7.6: processing failures are classified and retried in-process with backoff up
/// to MaxAttempts; permanent failures dead-letter immediately; the attempt counter is seeded from the
/// broker's <c>x-death</c> redelivery count. Phase 7.7: a terminal failure (permanent or exhausted)
/// publishes an enriched copy of the delivery to the dead-letter exchange (preserving body + headers
/// and adding reason/attempt/error metadata) and acknowledges the original; if the dead-letter publish
/// cannot be confirmed, the original is rejected (Nack requeue:false) so the broker dead-letters it
/// verbatim — a terminal failure is never silently lost and never retried endlessly.
/// Phase 7.8: every consumer outcome is logged with a consistent structured field set (MessageId,
/// EventId, EventType, EventVersion, CorrelationId, CausationId, Consumer, Attempt, Result, DurationMs,
/// FailureClassification, DeadLetterReason); business payloads are never logged; missing correlation/
/// causation metadata is processed safely; the envelope advertises the produced-event correlation/
/// causation propagation contract.
/// </summary>
public class IntegrationEventMessageProcessorTests
{
    private static readonly Guid CorrelationId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ProcessAsync_NewEvent_InvokesHandlerThroughIdempotencyBoundary_AndAcks()
    {
        var handler = new RecordingOrderCreatedHandler();
        var capturedMessage = default(ProcessedMessage);
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback((ProcessedMessage message, Func<Task> operation, CancellationToken token) => capturedMessage = message)
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(1, handler.InvocationCount);
        Assert.Equal(@event, handler.LastEvent);
        Assert.Equal(@event.EventId, handler.LastEnvelope!.EventId);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, handler.LastEnvelope.EventType);
        Assert.Equal(EventVersion.V1, handler.LastEnvelope.EventVersion);
        Assert.Equal(CorrelationId, handler.LastEnvelope.CorrelationId);
        Assert.Null(handler.LastEnvelope.CausationId);
        Assert.Equal(@event.OccurredAtUtc, handler.LastEnvelope.OccurredAtUtc);

        Assert.Equal(@event.EventId.ToString(), capturedMessage!.Key);
        Assert.Equal(RecordingOrderCreatedHandler.StableHandlerName, capturedMessage.Handler);
    }

    [Fact]
    public async Task ProcessAsync_Duplicate_Acknowledges_WithoutReExecuting()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(IdempotencyResult.AlreadyProcessed(DateTime.UtcNow.AddMinutes(-1))));

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_UnknownEventType_IsAcknowledgedAndSkipped()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        using var scope = BuildProcessorScope(handler, processorMock.Object, registerOrderCreated: false);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var properties = new BasicProperties
        {
            Type = "unhandled.event_type",
            Headers = new Dictionary<string, object?> { ["EventType"] = "unhandled.event_type" }
        };

        var action = await processor.ProcessAsync(properties, Array.Empty<byte>(), CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);
        processorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_DeliveryWithoutEventType_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(new BasicProperties(), Array.Empty<byte>(), CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_MissingEventIdHeader_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var @event = CreateOrderCreated();

        var properties = BuildProperties(@event);
        properties.Headers!.Remove("EventId");

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_MalformedEventVersionHeader_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var @event = CreateOrderCreated();

        var properties = BuildProperties(@event);
        properties.Headers!["EventVersion"] = "not-a-version";

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_MalformedOccurredAtHeader_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var @event = CreateOrderCreated();

        var properties = BuildProperties(@event);
        properties.Headers!["OccurredAtUtc"] = "not-a-date";

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_MalformedPayload_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            new byte[] { 0x01, 0x02, 0x03 },
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_PayloadMetadataDisagreeingWithEnvelope_IsRejected()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var @event = CreateOrderCreated();
        var body = JsonSerializer.SerializeToUtf8Bytes(@event with { EventId = Guid.NewGuid() });

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(BuildProperties(@event), body, CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Reject, action);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_RetryableFailure_IsRetried_ThenAcknowledged()
    {
        var handler = new RecordingOrderCreatedHandler { RemainingFailures = 1 };
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero });
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(2, handler.InvocationCount);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_RetryableFailure_ExhaustsRetries_ThenDeadLetters_WithMetadata()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();
        IReadOnlyDictionary<string, object?>? capturedHeaders = null;

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Business processing failed."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyBasicProperties p, ReadOnlyMemory<byte> b, string r, IReadOnlyDictionary<string, object?> h, CancellationToken c) => capturedHeaders = h)
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero },
            deadLetterer: deadLettererMock);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        deadLettererMock.Verify(
            d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                OrderCreatedIntegrationEvent.EventType,
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedHeaders);
        Assert.Equal(IntegrationEventMessageProcessor.ExhaustedReason, capturedHeaders[IntegrationEventMessageProcessor.DeadLetterReasonHeader]);
        Assert.Equal(3, capturedHeaders[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]);
        Assert.Equal(RecordingOrderCreatedHandler.StableHandlerName, capturedHeaders[IntegrationEventMessageProcessor.DeadLetterConsumerHeader]);
        Assert.Equal(nameof(InvalidOperationException), capturedHeaders[IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader]);
        Assert.Equal("Business processing failed.", capturedHeaders[IntegrationEventMessageProcessor.DeadLetterErrorMessageHeader]);
        Assert.False(string.IsNullOrWhiteSpace((string?)capturedHeaders[IntegrationEventMessageProcessor.DeadLetterAtHeader]));
    }

    [Fact]
    public async Task ProcessAsync_BusinessOperationFailure_ExhaustsRetries_ThenDeadLetters()
    {
        var handler = new RecordingOrderCreatedHandler { RemainingFailures = int.MaxValue };
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero });
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(3, handler.InvocationCount);
    }

    [Fact]
    public async Task ProcessAsync_PermanentFailure_DeadLettersImmediately_WithMetadata()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();
        IReadOnlyDictionary<string, object?>? capturedHeaders = null;

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Permanent failure."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyBasicProperties p, ReadOnlyMemory<byte> b, string r, IReadOnlyDictionary<string, object?> h, CancellationToken c) => capturedHeaders = h)
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions
            {
                MaxAttempts = 3,
                InitialDelay = TimeSpan.Zero,
                NonRetryableExceptionTypes = new() { typeof(InvalidOperationException) }
            },
            deadLetterer: deadLettererMock);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedHeaders);
        Assert.Equal(IntegrationEventMessageProcessor.PermanentFailureReason, capturedHeaders[IntegrationEventMessageProcessor.DeadLetterReasonHeader]);
        Assert.Equal(1, capturedHeaders[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]);
    }

    [Fact]
    public async Task ProcessAsync_RedeliveredMessage_CountsPriorAttempts_TowardsExhaustion()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();
        IReadOnlyDictionary<string, object?>? capturedHeaders = null;

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transient failure."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyBasicProperties p, ReadOnlyMemory<byte> b, string r, IReadOnlyDictionary<string, object?> h, CancellationToken c) => capturedHeaders = h)
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();
        var properties = BuildProperties(@event);
        properties.Headers!["x-death"] = new List<object>
        {
            new Dictionary<string, object> { ["count"] = 2L }
        };

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero },
            deadLetterer: deadLettererMock);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(3, capturedHeaders![IntegrationEventMessageProcessor.DeadLetterAttemptHeader]);
    }

    [Fact]
    public async Task ProcessAsync_MultipleXDeathEntries_UseMaximumCount()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();
        IReadOnlyDictionary<string, object?>? capturedHeaders = null;

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transient failure."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyBasicProperties p, ReadOnlyMemory<byte> b, string r, IReadOnlyDictionary<string, object?> h, CancellationToken c) => capturedHeaders = h)
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();
        var properties = BuildProperties(@event);
        properties.Headers!["x-death"] = new List<object>
        {
            new Dictionary<string, object> { ["count"] = 2L, ["reason"] = "rejected", ["exchange"] = "q.a.dlx", ["queue"] = "q.a.dlq" },
            new Dictionary<string, object> { ["count"] = 5L, ["reason"] = "expired", ["exchange"] = "q.b.dlx", ["queue"] = "q.b.dlq" }
        };

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 6, InitialDelay = TimeSpan.Zero },
            deadLetterer: deadLettererMock);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(6, capturedHeaders![IntegrationEventMessageProcessor.DeadLetterAttemptHeader]);
    }

    [Fact]
    public async Task ProcessAsync_DeadLetterPublishFails_FallsBackToBrokerDeadLetter()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Business processing failed."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var @event = CreateOrderCreated();

        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero },
            deadLetterer: deadLettererMock);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.DeadLetter, action);
        Assert.Equal(0, handler.InvocationCount);
        processorMock.Verify(
            p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        deadLettererMock.Verify(
            d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ByteArrayHeaders_AreDecoded()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();
        var properties = new BasicProperties
        {
            Type = OrderCreatedIntegrationEvent.EventType,
            Headers = new Dictionary<string, object?>
            {
                ["EventId"] = Encoding.UTF8.GetBytes(@event.EventId.ToString()),
                ["EventType"] = Encoding.UTF8.GetBytes(OrderCreatedIntegrationEvent.EventType),
                ["EventVersion"] = Encoding.UTF8.GetBytes(@event.Version.ToString()),
                ["CorrelationId"] = Encoding.UTF8.GetBytes(@event.CorrelationId.ToString()),
                ["CausationId"] = Encoding.UTF8.GetBytes(string.Empty),
                ["OccurredAtUtc"] = Encoding.UTF8.GetBytes(@event.OccurredAtUtc.ToString("O"))
            }
        };

        using var scope = BuildProcessorScope(handler, processorMock.Object);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(1, handler.InvocationCount);
        Assert.Equal(@event.EventId, handler.LastEnvelope!.EventId);
        Assert.Equal(CorrelationId, handler.LastEnvelope.CorrelationId);
        Assert.Null(handler.LastEnvelope.CausationId);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_OnSuccess_IncludesFullTelemetryFieldSet()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();
        var properties = BuildProperties(@event);
        properties.MessageId = "test-message-1";

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(handler, processorMock.Object, capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);

        var entry = captured.Single(LogLevel.Information);
        Assert.NotNull(entry);
        Assert.Equal(IntegrationEventMessageProcessor.ResultProcessed, entry["Result"]);
        Assert.Equal(RecordingOrderCreatedHandler.StableHandlerName, entry["Consumer"]);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, entry["EventType"]);
        Assert.Equal(@event.EventId, entry["EventId"]);
        Assert.Equal("test-message-1", entry["MessageId"]);
        Assert.Equal(EventVersion.V1, entry["EventVersion"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Null(entry["CausationId"]);
        Assert.Equal(1, entry["Attempt"]);
        Assert.IsType<long>(entry["DurationMs"]);
        Assert.True((long)entry["DurationMs"]! >= 0);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_OnDuplicate_RecordsDuplicateResult()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(IdempotencyResult.AlreadyProcessed(DateTime.UtcNow.AddMinutes(-1))));

        var @event = CreateOrderCreated();

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(handler, processorMock.Object, capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(0, handler.InvocationCount);

        var entry = captured.Single(LogLevel.Information);
        Assert.NotNull(entry);
        Assert.Equal(IntegrationEventMessageProcessor.ResultDuplicate, entry["Result"]);
        Assert.Equal(1, entry["Attempt"]);
        Assert.NotNull(entry["ProcessedAtUtc"]);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_OnRetryableFailure_IncludesAttemptAndMaxAttempts()
    {
        var handler = new RecordingOrderCreatedHandler { RemainingFailures = 1 };
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero },
            capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(2, handler.InvocationCount);

        var retrying = captured.Entries.Single(entry => entry.Level == LogLevel.Warning);
        Assert.Equal(IntegrationEventMessageProcessor.ResultRetrying, retrying["Result"]);
        Assert.Equal(1, retrying["Attempt"]);
        Assert.Equal(3, retrying["MaxAttempts"]);
        Assert.Equal(ConsumerFailureClassification.Retryable, retrying["FailureClassification"]);
        Assert.IsType<TimeSpan>(retrying["Delay"]);

        var processed = captured.Entries.Single(entry => entry.Level == LogLevel.Information);
        Assert.Equal(IntegrationEventMessageProcessor.ResultProcessed, processed["Result"]);
        Assert.Equal(2, processed["Attempt"]);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_OnPermanentFailure_RecordsDeadLetterClassificationAndReason()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Permanent failure."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions
            {
                MaxAttempts = 3,
                InitialDelay = TimeSpan.Zero,
                NonRetryableExceptionTypes = new() { typeof(InvalidOperationException) }
            },
            deadLetterer: deadLettererMock,
            capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);

        var entry = captured.Single(LogLevel.Error);
        Assert.NotNull(entry);
        Assert.Equal(IntegrationEventMessageProcessor.ResultDeadLettered, entry["Result"]);
        Assert.Equal(IntegrationEventMessageProcessor.PermanentFailureReason, entry["DeadLetterReason"]);
        Assert.Equal(1, entry["Attempt"]);
        Assert.Equal(ConsumerFailureClassification.Permanent, entry["FailureClassification"]);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_OnExhaustedRetries_RecordsDeadLetterReason()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();
        var deadLettererMock = new Mock<IIntegrationEventDeadLetterer>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Business processing failed."));

        deadLettererMock
            .Setup(d => d.PublishAsync(
                It.IsAny<IReadOnlyBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var @event = CreateOrderCreated();

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(
            handler,
            processorMock.Object,
            retry: new ConsumerRetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero },
            deadLetterer: deadLettererMock,
            capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            BuildProperties(@event),
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);

        var entry = captured.Entries.Single(logged => logged.Level == LogLevel.Error);
        Assert.NotNull(entry);
        Assert.Equal(IntegrationEventMessageProcessor.ResultDeadLettered, entry["Result"]);
        Assert.Equal(IntegrationEventMessageProcessor.ExhaustedReason, entry["DeadLetterReason"]);
        Assert.Equal(3, entry["Attempt"]);
        Assert.Equal(ConsumerFailureClassification.Retryable, entry["FailureClassification"]);
    }

    [Fact]
    public async Task ProcessAsync_MissingCorrelationMetadata_IsProcessedSafely()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated() with { CorrelationId = Guid.Empty };
        var properties = BuildProperties(@event);
        properties.Headers!["CorrelationId"] = string.Empty;
        properties.Headers!["CausationId"] = string.Empty;

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(handler, processorMock.Object, capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.Equal(1, handler.InvocationCount);
        Assert.Equal(Guid.Empty, handler.LastEnvelope!.CorrelationId);
        Assert.Null(handler.LastEnvelope.CausationId);

        var entry = captured.Single(LogLevel.Information);
        Assert.NotNull(entry);
        Assert.Equal(Guid.Empty, entry["CorrelationId"]);
        Assert.Null(entry["CausationId"]);
    }

    [Fact]
    public async Task ProcessAsync_StructuredLog_NeverLogsBusinessPayloads()
    {
        var handler = new RecordingOrderCreatedHandler();
        var processorMock = new Mock<IIdempotentProcessor>();

        processorMock
            .Setup(p => p.TryProcessAsync(It.IsAny<ProcessedMessage>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProcessedMessage message, Func<Task> operation, CancellationToken token) =>
            {
                await operation();
                return IdempotencyResult.NotProcessed();
            });

        var @event = CreateOrderCreated();
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event);
        var payloadJson = JsonSerializer.Serialize(@event);

        var captured = new CapturingLogger<IntegrationEventMessageProcessor>();
        using var scope = BuildProcessorScope(handler, processorMock.Object, capturedLogger: captured);
        var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();

        var action = await processor.ProcessAsync(BuildProperties(@event), payload, CancellationToken.None);

        Assert.Equal(ConsumerMessageAction.Ack, action);
        Assert.NotEmpty(captured.Entries);
        Assert.All(
            captured.Entries,
            logged =>
            {
                Assert.DoesNotContain("ORD-100", logged.Message);
                Assert.DoesNotContain(payloadJson, logged.Message);
                Assert.DoesNotContain(payloadJson, (string?)logged.Properties.FirstOrDefault(pair => pair.Key == "Payload").Value);
            });
    }

    [Fact]
    public void Envelope_ExposesProducedEventPropagationContract()
    {
        var @event = CreateOrderCreated();
        var envelope = new IntegrationEventEnvelope
        {
            EventId = @event.EventId,
            EventType = OrderCreatedIntegrationEvent.EventType,
            EventVersion = EventVersion.V1,
            CorrelationId = CorrelationId,
            CausationId = null,
            OccurredAtUtc = @event.OccurredAtUtc
        };

        Assert.Equal(CorrelationId, envelope.ProducedEventCorrelationId);
        Assert.Equal(@event.EventId, envelope.ProducedEventCausationId);
    }

    private static IServiceScope BuildProcessorScope(
        RecordingOrderCreatedHandler handler,
        IIdempotentProcessor processor,
        bool registerOrderCreated = true,
        ConsumerRetryOptions? retry = null,
        Mock<IIntegrationEventDeadLetterer>? deadLetterer = null,
        CapturingLogger<IntegrationEventMessageProcessor>? capturedLogger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (capturedLogger is not null)
            services.AddSingleton<ILogger<IntegrationEventMessageProcessor>>(capturedLogger);
        services.AddScoped(_ => handler);
        services.AddScoped(_ => processor);

        var registrations = new List<IntegrationEventConsumerDescriptorRegistration>();
        if (registerOrderCreated)
        {
            registrations.Add(new IntegrationEventConsumerDescriptorRegistration(
                new IntegrationEventConsumerDescriptor
                {
                    EventType = OrderCreatedIntegrationEvent.EventType,
                    EventContractType = typeof(OrderCreatedIntegrationEvent),
                    HandlerType = typeof(RecordingOrderCreatedHandler)
                }));
        }

        var registry = new IntegrationEventConsumerRegistry(registrations);
        services.AddSingleton(registry);
        services.AddSingleton<IIntegrationEventConsumerRegistry>(_ => registry);

        var options = new RabbitMQConsumerOptions
        {
            ExchangeName = "fluxcommerce.events",
            QueueName = "q.orders",
            Retry = retry ?? new ConsumerRetryOptions { InitialDelay = TimeSpan.Zero }
        };
        services.AddSingleton<IOptions<RabbitMQConsumerOptions>>(Options.Create(options));
        services.AddSingleton<IIntegrationEventFailureClassifier, IntegrationEventFailureClassifier>();

        var deadLettererMock = deadLetterer ?? new Mock<IIntegrationEventDeadLetterer>();
        if (deadLetterer is null)
        {
            deadLettererMock
                .Setup(d => d.PublishAsync(
                    It.IsAny<IReadOnlyBasicProperties>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, object?>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        services.AddSingleton(deadLettererMock.Object);
        services.AddScoped<IIntegrationEventMessageProcessor, IntegrationEventMessageProcessor>();

        return services.BuildServiceProvider().CreateScope();
    }

    private static BasicProperties BuildProperties(OrderCreatedIntegrationEvent @event) => new()
    {
        Type = OrderCreatedIntegrationEvent.EventType,
        Headers = new Dictionary<string, object?>
        {
            ["EventId"] = @event.EventId.ToString(),
            ["EventType"] = OrderCreatedIntegrationEvent.EventType,
            ["EventVersion"] = @event.Version.ToString(),
            ["CorrelationId"] = @event.CorrelationId.ToString(),
            ["CausationId"] = @event.CausationId?.ToString() ?? string.Empty,
            ["OccurredAtUtc"] = @event.OccurredAtUtc.ToString("O")
        }
    };

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-100",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId,
            Version = EventVersion.V1
        };

    public sealed class RecordingOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        public const string StableHandlerName = "UnitTests.ProjectOrderSummary";

        public string HandlerName => StableHandlerName;

        /// <summary>How many of the next invocations fail (decremented on each failure). 0 = never fail.</summary>
        public int RemainingFailures { get; init; }

        public int InvocationCount { get; private set; }

        public OrderCreatedIntegrationEvent? LastEvent { get; private set; }

        public IntegrationEventEnvelope? LastEnvelope { get; private set; }

        public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            LastEvent = @event;
            LastEnvelope = envelope;
            InvocationCount++;
            return InvocationCount <= RemainingFailures
                ? Task.FromException(new InvalidOperationException("Business processing failed."))
                : Task.CompletedTask;
        }
    }
}