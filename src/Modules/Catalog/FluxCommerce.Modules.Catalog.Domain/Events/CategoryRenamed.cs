using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class CategoryRenamed : DomainEvent
{
    public Guid CategoryId { get; }

    public string Name { get; }

    public CategoryRenamed(Guid categoryId, string name)
    {
        CategoryId = categoryId;
        Name = name;
    }
}
