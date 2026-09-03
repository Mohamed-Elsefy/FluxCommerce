using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductImagesReordered : DomainEvent
{
    public Guid ProductId { get; }

    public ProductImagesReordered(Guid productId)
    {
        ProductId = productId;
    }
}
