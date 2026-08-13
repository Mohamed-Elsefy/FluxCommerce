using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.ValueObjects;

public sealed class OrderId : ValueObject
{
    public Guid Value { get; }

    public OrderId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("OrderId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static OrderId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
