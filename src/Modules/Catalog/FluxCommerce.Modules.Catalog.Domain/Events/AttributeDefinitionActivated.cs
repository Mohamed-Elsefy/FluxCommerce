using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class AttributeDefinitionActivated : DomainEvent
{
    public Guid AttributeDefinitionId { get; }

    public AttributeDefinitionActivated(Guid attributeDefinitionId)
    {
        AttributeDefinitionId = attributeDefinitionId;
    }
}
