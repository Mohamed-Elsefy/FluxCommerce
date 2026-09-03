using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.BuildingBlocks.Domain.Entities;

public abstract class AggregateRoot<TKey> : Entity<TKey>, IAggregateRoot
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TKey id) : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() =>
        _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    protected void RemoveDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Remove(domainEvent);

    public void ClearDomainEvents() =>
        _domainEvents.Clear();
}
