using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Events;

/// <summary>
/// Abstract base for integration events. It is declared as a record so that value equality covers the
/// inherited metadata (a round-tripped event compares equal to the original), and its metadata
/// properties use <c>init</c> so System.Text.Json can repopulate them when deserializing a concrete
/// derived event (which declares only its business payload). Derived records construct with the
/// parameterless base and set metadata via the object initializer, e.g.
/// <c>new OrderCreatedIntegrationEvent(...) { CorrelationId = correlationId }</c>.
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public EventVersion Version { get; init; } = EventVersion.V1;

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    protected IntegrationEvent()
    {
    }

    protected IntegrationEvent(Guid correlationId, Guid? causationId = null, EventVersion? version = null)
    {
        CorrelationId = correlationId;
        CausationId = causationId;
        Version = version ?? EventVersion.V1;
    }
}