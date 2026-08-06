using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductPrimaryImageChanged : DomainEvent
{
    public Guid ProductId { get; }

    public Guid ImageId { get; }

    public ProductPrimaryImageChanged(Guid productId, Guid imageId)
    {
        ProductId = productId;
        ImageId = imageId;
    }
}
