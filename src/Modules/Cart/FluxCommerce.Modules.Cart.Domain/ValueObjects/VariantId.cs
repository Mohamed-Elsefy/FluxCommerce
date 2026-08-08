using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.ValueObjects;

public sealed class VariantId : ValueObject
{
    public Guid Value { get; }

    public VariantId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("VariantId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static VariantId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
