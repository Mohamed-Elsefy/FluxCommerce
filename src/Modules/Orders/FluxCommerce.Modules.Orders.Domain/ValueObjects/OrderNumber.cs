using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class OrderNumber : ValueObject
{
    public const int MaxLength = 32;

    public string Value { get; }

    public OrderNumber(string? value)
    {
        var orderNumber = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(orderNumber))
            throw new ArgumentException("An order number is required.", nameof(value));

        if (orderNumber.Length > MaxLength)
            throw new ArgumentException($"An order number must not exceed {MaxLength} characters.", nameof(value));

        Value = orderNumber;
    }

    public static OrderNumber Create() => new($"ORD-{Guid.NewGuid():N}"[..Math.Min(MaxLength, 4 + 12)]);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
