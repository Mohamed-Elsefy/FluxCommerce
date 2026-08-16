using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 7.2: the transport-agnostic consumer contract and processing model. Verifies the envelope
/// (processing context) built from canonical metadata, the handler contract shape, the descriptor and
/// registry resolution model, and the R2 decision — idempotency identity = <c>EventId + HandlerName</c>,
/// composed via the canonical <see cref="ProcessedMessage"/> without touching the publisher.
/// </summary>
public class ConsumerProcessingModelTests
{
    private static readonly Guid CorrelationId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class OrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        public const string StableHandlerName = "Orders.ProjectOrderSummary";

        public string HandlerName => StableHandlerName;

        public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // ---- Processing context (envelope) ----

    [Fact]
    public void Envelope_From_BuildsProcessingContext_FromCanonicalMetadata()
    {
        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-1",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId,
            Version = EventVersion.V1
        };

        var envelope = IntegrationEventEnvelope.From(@event, OrderCreatedIntegrationEvent.EventType);

        Assert.Equal(@event.EventId, envelope.EventId);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, envelope.EventType);
        Assert.Equal(EventVersion.V1, envelope.EventVersion);
        Assert.Equal(CorrelationId, envelope.CorrelationId);
        Assert.Equal(@event.CausationId, envelope.CausationId);
        Assert.Equal(@event.OccurredAtUtc, envelope.OccurredAtUtc);
    }

    // ---- Handler contract ----

    [Fact]
    public void Handler_ExposesStableHandlerName_AndInvokesWithEventAndEnvelope()
    {
        IIntegrationEventHandler<OrderCreatedIntegrationEvent> handler = new OrderCreatedHandler();

        Assert.Equal(OrderCreatedHandler.StableHandlerName, handler.HandlerName);

        var envelope = IntegrationEventEnvelope.From(CreateOrderCreated(), OrderCreatedIntegrationEvent.EventType);
        var completed = handler.HandleAsync(CreateOrderCreated(), envelope, CancellationToken.None);

        Assert.True(completed.IsCompletedSuccessfully);
    }

    // ---- Descriptor & registry resolution ----

    [Fact]
    public void Registry_ResolvesDescriptor_ByEventType()
    {
        var registry = new TestConsumerRegistry();

        var descriptor = registry.Resolve(OrderCreatedIntegrationEvent.EventType);

        Assert.NotNull(descriptor);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, descriptor.EventType);
        Assert.Equal(typeof(OrderCreatedIntegrationEvent), descriptor.EventContractType);
        Assert.Equal(typeof(OrderCreatedHandler), descriptor.HandlerType);
    }

    [Fact]
    public void Registry_ReturnsNull_ForUnknownEventType()
    {
        var registry = new TestConsumerRegistry();

        Assert.Null(registry.Resolve("unknown.event_type"));
    }

    // ---- R2: idempotency identity = EventId + HandlerName (composition, publisher untouched) ----

    [Fact]
    public void ProcessedMessage_Key_ComposesEventId_AndScopesByHandlerName()
    {
        var envelope = IntegrationEventEnvelope.From(CreateOrderCreated(), OrderCreatedIntegrationEvent.EventType);
        var handler = new OrderCreatedHandler();

        var processed = ProcessedMessage.Create(envelope.EventId, handler.HandlerName);

        Assert.Equal(envelope.EventId.ToString(), processed.Key);
        Assert.Equal(handler.HandlerName, processed.Handler);
        Assert.InRange(processed.ProcessedAtUtc, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void SameEvent_DifferentHandlers_Yield_DistinctIdempotencyIdentities()
    {
        var envelope = IntegrationEventEnvelope.From(CreateOrderCreated(), OrderCreatedIntegrationEvent.EventType);

        var summaryHandler = new OrderCreatedHandler();
        var projectionHandler = new SecondOrderCreatedHandler();

        var first = ProcessedMessage.Create(envelope.EventId, summaryHandler.HandlerName);
        var second = ProcessedMessage.Create(envelope.EventId, projectionHandler.HandlerName);

        Assert.Equal(first.Key, second.Key);
        Assert.NotEqual(first.Handler, second.Handler);
    }

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-100",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

    private sealed class SecondOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        public string HandlerName => "Orders.ProjectOrderHistory";

        public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestConsumerRegistry : IIntegrationEventConsumerRegistry
    {
        public IntegrationEventConsumerDescriptor? Resolve(string eventType) =>
            eventType == OrderCreatedIntegrationEvent.EventType
                ? new IntegrationEventConsumerDescriptor
                {
                    EventType = eventType,
                    EventContractType = typeof(OrderCreatedIntegrationEvent),
                    HandlerType = typeof(OrderCreatedHandler)
                }
                : null;
    }
}