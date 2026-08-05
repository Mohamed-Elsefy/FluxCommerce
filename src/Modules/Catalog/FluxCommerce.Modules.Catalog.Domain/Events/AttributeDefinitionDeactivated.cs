using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class AttributeDefinitionDeactivated : DomainEvent
{
    public Guid AttributeDefinitionId { get; }

    public AttributeDefinitionDeactivated(Guid attributeDefinitionId)
    {
        AttributeDefinitionId = attributeDefinitionId;
    }
}
