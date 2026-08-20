using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;

namespace FluxCommerce.BuildingBlocks.Outbox.Mapping;

/// <summary>
/// Serializes a fully-formed <see cref="IIntegrationEvent"/> into an <see cref="OutboxMessage"/> record
/// (Phase 8.5). Unlike the domain-event outbox factories (which map selected domain events and inject
/// the correlation id), the integration event already carries the canonical metadata its producer
/// assigned — EventId, CorrelationId, CausationId, OccurredAtUtc — so the factory only snapshots it
/// into the outbox wire document. The event type argument is the stable wire type identifier (the
/// event's <c>EventType</c> const), also stored in the Outbox <c>Type</c> column and used as the
/// publisher's routing key. The factory is stateless and safe to register as a singleton.
/// </summary>
/// <remarks>
/// Phase 8.9: the W3C Trace Context of the producing activity (<c>traceparent</c>/<c>tracestate</c>) is
/// snapshotted onto the outbox record at staging time, so the dispatcher can restore it and the trace
/// survives the asynchronous outbox hop. Only a W3C-formatted activity id is captured (W3C is the default
/// in .NET); the check mirrors <c>TraceContext</c> in <c>BuildingBlocks.Infrastructure</c> because the
/// outbox assembly does not reference it.
/// </remarks>
public sealed class IntegrationEventOutboxMessageFactory
{
    public OutboxMessage Create(string eventType, IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("An event type must be provided.", nameof(eventType));

        var current = Activity.Current;
        var traceParent = current is { IdFormat: ActivityIdFormat.W3C } ? current.Id : null;
        var traceState = current?.TraceStateString;

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = integrationEvent.EventId,
            Type = eventType,
            Payload = JsonSerializer.Serialize((object)integrationEvent),
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