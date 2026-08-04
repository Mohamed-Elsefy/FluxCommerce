using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class VariantSkuChanged : DomainEvent
{
    public Guid VariantId { get; }

    public string Sku { get; }

    public VariantSkuChanged(Guid variantId, string sku)
    {
        VariantId = variantId;
        Sku = sku;
    }
}
