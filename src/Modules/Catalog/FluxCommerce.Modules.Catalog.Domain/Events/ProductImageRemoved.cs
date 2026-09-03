using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductImageRemoved : DomainEvent
{
    public Guid ProductId { get; }

    public Guid ImageId { get; }

    public ProductImageRemoved(Guid productId, Guid imageId)
    {
        ProductId = productId;
        ImageId = imageId;
    }
}
