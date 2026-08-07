using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.ValueObjects;

public sealed class CartId : ValueObject
{
    public Guid Value { get; }

    public CartId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CartId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static CartId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
