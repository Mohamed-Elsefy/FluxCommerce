using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }

    public string Currency { get; }

    public Money(decimal amount, string? currency)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "A monetary amount cannot be negative.");

        var normalizedCurrency = currency?.Trim().ToUpperInvariant();

        if (normalizedCurrency is null || !IsValidCurrencyCode(normalizedCurrency))
            throw new ArgumentException(
                "A valid three-letter ISO 4217 currency code is required.",
                nameof(currency));

        Amount = amount;
        Currency = normalizedCurrency;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    private static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: 3 } &&
        currency.All(character => character is >= 'A' and <= 'Z');
}
