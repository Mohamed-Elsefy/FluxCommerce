using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.ValueObjects;

public sealed class Sku : ValueObject
{
    public const int MaxLength = 64;

    public string Value { get; }

    public Sku(string? value)
    {
        var sku = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(sku))
            throw new ArgumentException("SKU is required.", nameof(value));

        if (sku.Length > MaxLength)
            throw new ArgumentException($"SKU must not exceed {MaxLength} characters.", nameof(value));

        if (!IsValidSkuFormat(sku))
            throw new ArgumentException(
                "SKU must contain only uppercase letters, digits, hyphens, underscores and dots.",
                nameof(value));

        Value = sku;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool IsValidSkuFormat(string sku)
    {
        foreach (var character in sku)
        {
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.')
                continue;

            return false;
        }

        return true;
    }
}
