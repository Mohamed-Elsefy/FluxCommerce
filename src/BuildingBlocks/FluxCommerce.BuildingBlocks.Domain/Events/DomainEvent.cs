namespace FluxCommerce.BuildingBlocks.Domain.Events;

public abstract class DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;
}
