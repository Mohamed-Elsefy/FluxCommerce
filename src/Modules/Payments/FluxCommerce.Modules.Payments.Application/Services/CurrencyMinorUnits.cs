namespace FluxCommerce.Modules.Payments.Application.Services;

/// <summary>
/// Converts major-unit money amounts to Stripe minor units (cents) in a
/// currency-aware way. Zero-decimal currencies (e.g. JPY) are sent as-is;
/// all others are scaled by 100 with banker's-bias-free rounding
/// (<see cref="MidpointRounding.AwayFromZero"/>).
/// </summary>
public static class CurrencyMinorUnits
{
    /// <summary>
    /// ISO 4217 zero-decimal currencies supported by Stripe (no minor unit).
    /// Source: https://stripe.com/docs/currencies#zero-decimal
    /// </summary>
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA",
        "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    /// <summary>
    /// Returns true when the currency has no minor unit on Stripe.
    /// </summary>
    public static bool IsZeroDecimal(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return ZeroDecimalCurrencies.Contains(currency.Trim());
    }

    /// <summary>
    /// Converts a major-unit amount (e.g. dollars) to Stripe minor units (e.g. cents).
    /// </summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (IsZeroDecimal(currency))
            return (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        return (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }
}
