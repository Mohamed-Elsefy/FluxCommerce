using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductArchived : DomainEvent
{
    public Guid ProductId { get; }

    public ProductArchived(Guid productId)
    {
        ProductId = productId;
    }
}
