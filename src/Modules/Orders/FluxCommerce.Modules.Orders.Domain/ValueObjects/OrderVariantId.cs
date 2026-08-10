using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class OrderVariantId : ValueObject
{
    public Guid Value { get; }

    public OrderVariantId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("VariantId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static OrderVariantId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
