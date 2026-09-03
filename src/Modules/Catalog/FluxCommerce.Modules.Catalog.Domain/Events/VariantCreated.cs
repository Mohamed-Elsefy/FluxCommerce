using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class VariantCreated : DomainEvent
{
    public Guid VariantId { get; }

    public VariantCreated(Guid variantId)
    {
        VariantId = variantId;
    }
}
