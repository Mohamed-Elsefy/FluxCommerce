using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Events;

public abstract class IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public Guid CorrelationId { get; }

    public Guid? CausationId { get; }

    public EventVersion Version { get; }

    public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;

    protected IntegrationEvent(Guid correlationId, Guid? causationId = null, EventVersion? version = null)
    {
        CorrelationId = correlationId;
        CausationId = causationId;
        Version = version ?? EventVersion.V1;
    }
}
