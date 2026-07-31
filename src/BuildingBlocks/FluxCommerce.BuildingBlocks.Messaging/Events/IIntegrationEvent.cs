using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Events;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    EventVersion Version { get; }
    DateTime OccurredAtUtc { get; }
}
