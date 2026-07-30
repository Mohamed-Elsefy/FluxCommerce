using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.BuildingBlocks.Domain.Entities;

public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> GetDomainEvents();
    void ClearDomainEvents();
}
