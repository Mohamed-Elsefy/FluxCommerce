using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 6.3 explicit Domain Event → Integration Event mapping for the Orders module: only the
/// selected events (OrderCreated, OrderCancelled) produce outbox records, and each record carries
/// the correct metadata (EventId, CorrelationId, timestamps, Type, Payload, initial Pending status).
/// </summary>
public class OrdersOutboxMessageFactoryTests
{
    private static readonly Guid CorrelationId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void OrderCreated_ProducesOutboxMessage_WithCorrectMetadata()
    {
        var orderId = OrderId.Create();
        var customerId = CustomerId.Create();
        var orderNumber = new OrderNumber("ORD-123");
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var domainEvent = new OrderCreated(orderId, customerId, orderNumber, occurredAt);

        var message = Assert.Single(new OrdersOutboxMessageFactory(CreateProvider()).Create([domainEvent]));

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(domainEvent.EventId, message.EventId);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, message.Type);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Null(message.CausationId);
        Assert.Equal(domainEvent.OccurredAtUtc, message.OccurredAtUtc);
        Assert.InRange(message.CreatedAtUtc, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.Error);

        var payload = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Payload);

        Assert.NotNull(payload);
        Assert.Equal(orderId.Value, payload!.OrderId);
        Assert.Equal(customerId.Value, payload.CustomerId);
        Assert.Equal(orderNumber.Value, payload.OrderNumber);
        Assert.Equal(occurredAt, payload.OccurredAt);
    }

    [Fact]
    public void OrderCancelled_ProducesOutboxMessage_WithCorrectMetadata()
    {
        var orderId = OrderId.Create();
        var customerId = CustomerId.Create();
        var orderNumber = new OrderNumber("ORD-456");
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var domainEvent = new OrderCancelled(orderId, customerId, orderNumber, occurredAt);

        var message = Assert.Single(new OrdersOutboxMessageFactory(CreateProvider()).Create([domainEvent]));

        Assert.Equal(domainEvent.EventId, message.EventId);
        Assert.Equal(OrderCancelledIntegrationEvent.EventType, message.Type);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Equal(OutboxStatus.Pending, message.Status);

        var payload = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(message.Payload);

        Assert.NotNull(payload);
        Assert.Equal(orderId.Value, payload!.OrderId);
        Assert.Equal(customerId.Value, payload.CustomerId);
        Assert.Equal(orderNumber.Value, payload.OrderNumber);
        Assert.Equal(occurredAt, payload.OccurredAt);
    }

    [Fact]
    public void UnmappedDomainEvent_ProducesNoOutboxMessage()
    {
        var messages = new OrdersOutboxMessageFactory(CreateProvider()).Create([new UnmappedDomainEvent()]);

        Assert.Empty(messages);
    }

    [Fact]
    public void MixedEvents_ProducesOnlyMappedEvents()
    {
        var domainEvents = new IDomainEvent[]
        {
            new OrderCreated(OrderId.Create(), CustomerId.Create(), new OrderNumber("ORD-A"), DateTimeOffset.UtcNow),
            new UnmappedDomainEvent(),
            new OrderCancelled(OrderId.Create(), CustomerId.Create(), new OrderNumber("ORD-B"), DateTimeOffset.UtcNow),
        };

        var messages = new OrdersOutboxMessageFactory(CreateProvider()).Create(domainEvents);

        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, message => message.Type == OrderCreatedIntegrationEvent.EventType);
        Assert.Contains(messages, message => message.Type == OrderCancelledIntegrationEvent.EventType);
    }

    private static ICorrelationIdProvider CreateProvider() => new FixedCorrelationIdProvider(CorrelationId);

    private sealed class FixedCorrelationIdProvider : ICorrelationIdProvider
    {
        private readonly Guid _value;

        public FixedCorrelationIdProvider(Guid value) => _value = value;

        public Guid GetCorrelationId() => _value;
    }

    private sealed class UnmappedDomainEvent : DomainEvent
    {
    }
}