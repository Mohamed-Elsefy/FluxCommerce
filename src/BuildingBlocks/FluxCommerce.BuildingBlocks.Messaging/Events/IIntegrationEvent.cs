namespace FluxCommerce.BuildingBlocks.Messaging.Events;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    string Version { get; }
    DateTime OccurredAtUtc { get; }
}
