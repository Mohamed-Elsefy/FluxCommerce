using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductSpecificationCreated : DomainEvent
{
    public Guid ProductId { get; }

    public Guid SpecificationId { get; }

    public string Name { get; }

    public string Value { get; }

    public ProductSpecificationCreated(Guid productId, Guid specificationId, string name, string value)
    {
        ProductId = productId;
        SpecificationId = specificationId;
        Name = name;
        Value = value;
    }
}
