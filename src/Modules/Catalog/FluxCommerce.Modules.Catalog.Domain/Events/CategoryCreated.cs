using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class CategoryCreated : DomainEvent
{
    public Guid CategoryId { get; }

    public CategoryCreated(Guid categoryId)
    {
        CategoryId = categoryId;
    }
}
