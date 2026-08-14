using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Events;

namespace FluxCommerce.Modules.Orders.Infrastructure.Outbox;

/// <summary>
/// Explicit Phase 6.3 mapping from the Orders domain events selected by the phase (order placed and
/// order cancelled) to <see cref="OutboxMessage"/> records. Since Phase 6.4 the mapped integration
/// contracts live in the module Contracts project and derive from the canonical messaging metadata
/// base; the correlation id is injected into the event here because domain events only carry
/// <c>EventId</c> + <c>OccurredAtUtc</c>. Events that are not selected produce no outbox record. The
/// records are added to the module DbContext by <see cref="OrdersOutboxSaveChangesInterceptor"/> so
/// they commit atomically with the business state.
/// </summary>
public sealed class OrdersOutboxMessageFactory
{
    private readonly ICorrelationIdProvider _correlationIdProvider;

    public OrdersOutboxMessageFactory(ICorrelationIdProvider correlationIdProvider)
    {
        _correlationIdProvider = correlationIdProvider ?? throw new ArgumentNullException(nameof(correlationIdProvider));
    }

    public IReadOnlyList<OutboxMessage> Create(IReadOnlyCollection<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        var messages = new List<OutboxMessage>();

        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case OrderCreated created:
                    messages.Add(Map(created));
                    break;
                case OrderCancelled cancelled:
                    messages.Add(Map(cancelled));
                    break;
            }
        }

        return messages;
    }

    private OutboxMessage Map(OrderCreated domainEvent)
    {
        var integrationEvent = new OrderCreatedIntegrationEvent(
            domainEvent.OrderId.Value,
            domainEvent.CustomerId.Value,
            domainEvent.OrderNumber.Value,
            domainEvent.OccurredAt)
        {
            CorrelationId = _correlationIdProvider.GetCorrelationId(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = domainEvent.EventId,
            Type = OrderCreatedIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            CorrelationId = integrationEvent.CorrelationId,
            CausationId = integrationEvent.CausationId,
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null
        };
    }

    private OutboxMessage Map(OrderCancelled domainEvent)
    {
        var integrationEvent = new OrderCancelledIntegrationEvent(
            domainEvent.OrderId.Value,
            domainEvent.CustomerId.Value,
            domainEvent.OrderNumber.Value,
            domainEvent.OccurredAt)
        {
            CorrelationId = _correlationIdProvider.GetCorrelationId(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = domainEvent.EventId,
            Type = OrderCancelledIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            CorrelationId = integrationEvent.CorrelationId,
            CausationId = integrationEvent.CausationId,
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null
        };
    }
}