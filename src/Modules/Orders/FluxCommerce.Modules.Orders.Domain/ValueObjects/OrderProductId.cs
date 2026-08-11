using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class OrderProductId : ValueObject
{
    public Guid Value { get; }

    public OrderProductId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ProductId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static OrderProductId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
