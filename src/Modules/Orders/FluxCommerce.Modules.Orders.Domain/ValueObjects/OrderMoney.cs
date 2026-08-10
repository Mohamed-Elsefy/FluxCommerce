using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class OrderMoney : ValueObject
{
    public decimal Amount { get; }

    public string Currency { get; }

    public OrderMoney(decimal amount, string? currency)
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

    public static OrderMoney Zero(string? currency) => new(0m, currency);

    public OrderMoney Add(OrderMoney other)
    {
        if (other is null)
            throw new ArgumentNullException(nameof(other));

        EnsureSameCurrency(other);

        return new OrderMoney(Amount + other.Amount, Currency);
    }

    public OrderMoney Subtract(OrderMoney other)
    {
        if (other is null)
            throw new ArgumentNullException(nameof(other));

        EnsureSameCurrency(other);

        if (other.Amount > Amount)
            throw new InvalidOperationException("A monetary amount cannot be negative.");

        return new OrderMoney(Amount - other.Amount, Currency);
    }

    public OrderMoney Multiply(decimal factor)
    {
        if (factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "A multiplication factor cannot be negative.");

        return new OrderMoney(Amount * factor, Currency);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    private void EnsureSameCurrency(OrderMoney other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot combine money in '{Currency}' with money in '{other.Currency}'.");
    }

    private static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: 3 } &&
        currency.All(character => character is >= 'A' and <= 'Z');
}
