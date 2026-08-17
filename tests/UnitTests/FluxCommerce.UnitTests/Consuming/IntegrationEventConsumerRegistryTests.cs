using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 7.5: the consumer registry materializes every <c>AddIntegrationEventConsumer&lt;,&gt;</c>
/// descriptor (contributed as DI descriptor registrations) and resolves them by the stable wire
/// <c>EventType</c> — the deterministic mapping the consumer host binds queues with and the message
/// processor dispatches through. Unknown event types resolve to <c>null</c> and are skipped.
/// </summary>
public class IntegrationEventConsumerRegistryTests
{
    [Fact]
    public void Resolve_ReturnsDescriptor_ForRegisteredEventType()
    {
        var registry = CreateRegistry();

        var descriptor = registry.Resolve(OrderCreatedIntegrationEvent.EventType);

        Assert.NotNull(descriptor);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, descriptor.EventType);
        Assert.Equal(typeof(OrderCreatedIntegrationEvent), descriptor.EventContractType);
        Assert.Equal(typeof(TestOrderCreatedHandler), descriptor.HandlerType);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUnknownEventType()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.Resolve("unknown.event_type"));
    }

    [Fact]
    public void GetDescriptors_EnumeratesAllRegisteredDescriptors()
    {
        var registry = CreateRegistry();

        var descriptors = registry.GetDescriptors();

        var descriptor = Assert.Single(descriptors);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, descriptor.EventType);
    }

    [Fact]
    public void Registry_WithNoRegistrations_ResolvesNothing()
    {
        var registry = new IntegrationEventConsumerRegistry([]);

        Assert.Null(registry.Resolve(OrderCreatedIntegrationEvent.EventType));
        Assert.Empty(registry.GetDescriptors());
    }

    private static IntegrationEventConsumerRegistry CreateRegistry() =>
        new(
        [
            new IntegrationEventConsumerDescriptorRegistration(
                new IntegrationEventConsumerDescriptor
                {
                    EventType = OrderCreatedIntegrationEvent.EventType,
                    EventContractType = typeof(OrderCreatedIntegrationEvent),
                    HandlerType = typeof(TestOrderCreatedHandler)
                })
        ]);

    public sealed class TestOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        public string HandlerName => "UnitTests.TestHandler";

        public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}