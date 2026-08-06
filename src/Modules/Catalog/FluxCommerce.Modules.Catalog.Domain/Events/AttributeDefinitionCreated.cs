using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class AttributeDefinitionCreated : DomainEvent
{
    public Guid AttributeDefinitionId { get; }

    public AttributeDefinitionCreated(Guid attributeDefinitionId)
    {
        AttributeDefinitionId = attributeDefinitionId;
    }
}
