using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Events;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Outbox;

/// <summary>
/// Explicit Phase 6.3 mapping from the Inventory domain events selected by the phase (reservation
/// expired) to <see cref="OutboxMessage"/> records. Since Phase 6.4 the mapped integration contract
/// lives in the module Contracts project and derives from the canonical messaging metadata base; the
/// correlation id is injected into the event here because domain events only carry <c>EventId</c> +
/// <c>OccurredAtUtc</c>. Events that are not selected produce no outbox record. The records are added
/// to the module DbContext by <see cref="InventoryOutboxSaveChangesInterceptor"/> so they commit
/// atomically with the business state (the reservation expiry is persisted by the Inventory store on a
/// single save).
/// </summary>
public sealed class InventoryOutboxMessageFactory
{
    private readonly ICorrelationIdProvider _correlationIdProvider;

    public InventoryOutboxMessageFactory(ICorrelationIdProvider correlationIdProvider)
    {
        _correlationIdProvider = correlationIdProvider ?? throw new ArgumentNullException(nameof(correlationIdProvider));
    }

    public IReadOnlyList<OutboxMessage> Create(IReadOnlyCollection<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        var messages = new List<OutboxMessage>();

        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent is InventoryReservationExpired expired)
            {
                messages.Add(Map(expired));
            }
        }

        return messages;
    }

    private OutboxMessage Map(InventoryReservationExpired domainEvent)
    {
        var integrationEvent = new InventoryReservationExpiredIntegrationEvent(
            domainEvent.ReservationId.Value,
            domainEvent.OrderId.Value,
            domainEvent.Sku.Value,
            domainEvent.Quantity,
            domainEvent.OccurredAt)
        {
            EventId = domainEvent.EventId,
            CorrelationId = _correlationIdProvider.GetCorrelationId(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };

        // Phase 11.9: snapshot the W3C Trace Context of the producing activity (traceparent/tracestate)
        // onto the outbox record at staging time so the Inventory outbox dispatcher can restore it as the
        // parent of the producer span and the reservation-expiry trace survives the outbox hop, matching the
        // Orders integration-event factory (Observability & Distributed Tracing).
        var current = Activity.Current;
        var traceParent = current is { IdFormat: ActivityIdFormat.W3C } ? current.Id : null;
        var traceState = current?.TraceStateString;

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = domainEvent.EventId,
            Type = InventoryReservationExpiredIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            CorrelationId = integrationEvent.CorrelationId,
            CausationId = integrationEvent.CausationId,
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null,
            TraceParent = traceParent,
            TraceState = traceState
        };
    }
}