using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Outbox;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 6.3 explicit Domain Event → Integration Event mapping for the Inventory module: only the
/// selected event (InventoryReservationExpired) produces an outbox record, and it carries the correct
/// metadata (EventId, CorrelationId, timestamps, Type, Payload, initial Pending status).
/// </summary>
public class InventoryOutboxMessageFactoryTests
{
    private static readonly Guid CorrelationId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ReservationExpired_ProducesOutboxMessage_WithCorrectMetadata()
    {
        var reservationId = InventoryReservationId.Create();
        var orderId = OrderId.Create();
        var sku = new InventorySku("SKU-1");
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var domainEvent = new InventoryReservationExpired(reservationId, orderId, sku, 3, occurredAt);

        var message = Assert.Single(new InventoryOutboxMessageFactory(CreateProvider()).Create([domainEvent]));

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(domainEvent.EventId, message.EventId);
        Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, message.Type);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Null(message.CausationId);
        Assert.Equal(domainEvent.OccurredAtUtc, message.OccurredAtUtc);
        Assert.InRange(message.CreatedAtUtc, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.Error);

        var payload = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(message.Payload);

        Assert.NotNull(payload);
        Assert.Equal(domainEvent.EventId, payload!.EventId);
        Assert.Equal(reservationId.Value, payload.ReservationId);
        Assert.Equal(orderId.Value, payload.OrderId);
        Assert.Equal("SKU-1", payload.Sku);
        Assert.Equal(3, payload.Quantity);
        Assert.Equal(occurredAt, payload.ExpiredAtUtc);
        Assert.Equal(domainEvent.OccurredAtUtc, payload.OccurredAtUtc);
        Assert.Equal(CorrelationId, payload.CorrelationId);
        Assert.Null(payload.CausationId);
    }

    [Fact]
    public void UnmappedDomainEvent_ProducesNoOutboxMessage()
    {
        var messages = new InventoryOutboxMessageFactory(CreateProvider()).Create([new UnmappedDomainEvent()]);

        Assert.Empty(messages);
    }

    [Fact]
    public void MixedEvents_ProducesOnlyMappedEvents()
    {
        var domainEvents = new IDomainEvent[]
        {
            new UnmappedDomainEvent(),
            new InventoryReservationExpired(
                InventoryReservationId.Create(),
                OrderId.Create(),
                new InventorySku("SKU-2"),
                2,
                DateTimeOffset.UtcNow),
        };

        var messages = new InventoryOutboxMessageFactory(CreateProvider()).Create(domainEvents);

        var message = Assert.Single(messages);
        Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, message.Type);
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