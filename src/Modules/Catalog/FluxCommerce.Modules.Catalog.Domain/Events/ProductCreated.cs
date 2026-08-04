using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductCreated : DomainEvent
{
    public Guid ProductId { get; }

    public ProductCreated(Guid productId)
    {
        ProductId = productId;
    }
}
