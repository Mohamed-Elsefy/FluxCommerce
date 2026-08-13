using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.ValueObjects;

public sealed class InventoryReservationId : ValueObject
{
    public Guid Value { get; }

    public InventoryReservationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ReservationId must not be an empty GUID.", nameof(value));

        Value = value;
    }

    public static InventoryReservationId Create() => new(Guid.NewGuid());

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
