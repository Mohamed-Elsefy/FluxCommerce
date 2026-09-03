using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class ProductSpecification : Entity<Guid>
{
    public const int NameMaxLength = 200;

    public const int ValueMaxLength = 1000;

    public string Name { get; private set; }

    public string Value { get; private set; }

    public DateTime CreatedAtUtc { get; }

    internal ProductSpecification(Guid id, string name, string value, DateTime utcNow)
        : base(id)
    {
        Name = name;
        Value = value;
        CreatedAtUtc = utcNow;
    }

    #pragma warning disable CS8618
    private ProductSpecification() { }
    #pragma warning restore CS8618

    internal void ChangeValue(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
