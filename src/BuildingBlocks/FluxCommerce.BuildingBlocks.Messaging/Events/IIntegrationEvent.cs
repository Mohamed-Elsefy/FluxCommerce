using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Events;

/// <summary>
/// Canonical metadata contract for an integration event: a stable, versioned, cross-module transport
/// document. Every concrete integration event derives from <see cref="IntegrationEvent"/> and adds its
/// business payload. The metadata surface (EventId, CorrelationId, CausationId, Version,
/// OccurredAtUtc) is the single source of truth for tracing and versioning; <c>EventType</c> is the
/// per-contract stable type identifier (see each concrete event's <c>EventType</c> constant and the
/// Outbox <c>Type</c> column).
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    EventVersion Version { get; }
    DateTime OccurredAtUtc { get; }
}