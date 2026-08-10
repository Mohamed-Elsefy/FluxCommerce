using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class CustomerId : ValueObject
{
    public Guid Value { get; }

    public CustomerId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CustomerId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static CustomerId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
