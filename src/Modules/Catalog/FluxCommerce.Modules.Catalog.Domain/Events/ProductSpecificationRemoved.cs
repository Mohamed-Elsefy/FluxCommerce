using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductSpecificationRemoved : DomainEvent
{
    public Guid ProductId { get; }

    public Guid SpecificationId { get; }

    public ProductSpecificationRemoved(Guid productId, Guid specificationId)
    {
        ProductId = productId;
        SpecificationId = specificationId;
    }
}
