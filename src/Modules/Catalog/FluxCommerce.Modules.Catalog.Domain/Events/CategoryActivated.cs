using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class CategoryActivated : DomainEvent
{
    public Guid CategoryId { get; }

    public CategoryActivated(Guid categoryId)
    {
        CategoryId = categoryId;
    }
}
