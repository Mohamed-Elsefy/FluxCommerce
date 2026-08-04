using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductActivated : DomainEvent
{
    public Guid ProductId { get; }

    public ProductActivated(Guid productId)
    {
        ProductId = productId;
    }
}
