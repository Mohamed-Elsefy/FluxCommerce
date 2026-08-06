using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductSpecificationUpdated : DomainEvent
{
    public Guid ProductId { get; }

    public Guid SpecificationId { get; }

    public string Name { get; }

    public string Value { get; }

    public ProductSpecificationUpdated(Guid productId, Guid specificationId, string name, string value)
    {
        ProductId = productId;
        SpecificationId = specificationId;
        Name = name;
        Value = value;
    }
}
