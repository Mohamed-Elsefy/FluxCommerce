using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductCategoryChanged : DomainEvent
{
    public Guid ProductId { get; }

    public Guid CategoryId { get; }

    public ProductCategoryChanged(Guid productId, Guid categoryId)
    {
        ProductId = productId;
        CategoryId = categoryId;
    }
}
