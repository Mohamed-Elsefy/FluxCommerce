using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class ProductAttributeValue : Entity<Guid>
{
    public const int ValueMaxLength = 500;

    public Guid AttributeDefinitionId { get; private set; }

    public string Value { get; private set; }

    public DateTime CreatedAtUtc { get; }

    internal ProductAttributeValue(Guid id, Guid attributeDefinitionId, string value, DateTime utcNow)
        : base(id)
    {
        AttributeDefinitionId = attributeDefinitionId;
        Value = value;
        CreatedAtUtc = utcNow;
    }

    #pragma warning disable CS8618
    private ProductAttributeValue() { }
    #pragma warning restore CS8618

    internal void ChangeValue(string value) => Value = value;
}
