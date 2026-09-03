using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductAttributeValueUpdated : DomainEvent
{
    public Guid ProductId { get; }

    public Guid AttributeDefinitionId { get; }

    public string Value { get; }

    public ProductAttributeValueUpdated(Guid productId, Guid attributeDefinitionId, string value)
    {
        ProductId = productId;
        AttributeDefinitionId = attributeDefinitionId;
        Value = value;
    }
}
