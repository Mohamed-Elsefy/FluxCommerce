using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class CategoryDeactivated : DomainEvent
{
    public Guid CategoryId { get; }

    public CategoryDeactivated(Guid categoryId)
    {
        CategoryId = categoryId;
    }
}
