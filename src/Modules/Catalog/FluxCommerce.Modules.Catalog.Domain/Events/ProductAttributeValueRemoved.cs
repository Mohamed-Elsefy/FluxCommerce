using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductAttributeValueRemoved : DomainEvent
{
    public Guid ProductId { get; }

    public Guid AttributeDefinitionId { get; }

    public ProductAttributeValueRemoved(Guid productId, Guid attributeDefinitionId)
    {
        ProductId = productId;
        AttributeDefinitionId = attributeDefinitionId;
    }
}
