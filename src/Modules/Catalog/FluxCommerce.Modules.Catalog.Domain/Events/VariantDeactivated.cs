using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class VariantDeactivated : DomainEvent
{
    public Guid VariantId { get; }

    public VariantDeactivated(Guid variantId)
    {
        VariantId = variantId;
    }
}
