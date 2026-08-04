using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class VariantActivated : DomainEvent
{
    public Guid VariantId { get; }

    public VariantActivated(Guid variantId)
    {
        VariantId = variantId;
    }
}
