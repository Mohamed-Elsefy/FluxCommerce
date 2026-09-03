using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductAttributeValueAssigned : DomainEvent
{
    public Guid ProductId { get; }

    public Guid AttributeDefinitionId { get; }

    public string Value { get; }

    public ProductAttributeValueAssigned(Guid productId, Guid attributeDefinitionId, string value)
    {
        ProductId = productId;
        AttributeDefinitionId = attributeDefinitionId;
        Value = value;
    }
}
