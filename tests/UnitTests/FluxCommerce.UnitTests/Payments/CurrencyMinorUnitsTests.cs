using FluxCommerce.Modules.Payments.Application.Services;
using FluxCommerce.Modules.Payments.Infrastructure.Gateways;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class CurrencyMinorUnitsTests
{
    [Theory]
    [InlineData("USD", 19.99, 1999)]
    [InlineData("usd", 19.99, 1999)]
    [InlineData("EUR", 10.00, 1000)]
    [InlineData("USD", 0.01, 1)]
    public void ToMinorUnits_TwoDecimalCurrency_ScalesBy100(string currency, decimal amount, long expected)
    {
        Assert.Equal(expected, CurrencyMinorUnits.ToMinorUnits(amount, currency));
    }

    [Theory]
    [InlineData("JPY", 1999, 1999)]
    [InlineData("jpy", 1500, 1500)]
    [InlineData("KRW", 10000, 10000)]
    [InlineData("VND", 50000, 50000)]
    public void ToMinorUnits_ZeroDecimalCurrency_SendsAmountAsIs(string currency, decimal amount, long expected)
    {
        Assert.Equal(expected, CurrencyMinorUnits.ToMinorUnits(amount, currency));
    }

    [Fact]
    public void ToMinorUnits_FractionalMill_RoundsAwayFromZeroInsteadOfTruncating()
    {
        // Old code ((long)(amount * 100m)) truncated 19.999 -> 1999; correct rounding gives 2000.
        Assert.Equal(2000, CurrencyMinorUnits.ToMinorUnits(19.999m, "USD"));
        Assert.Equal(1999, CurrencyMinorUnits.ToMinorUnits(19.991m, "USD"));
    }

    [Fact]
    public void ToMinorUnits_ZeroDecimalFraction_RoundsToWholeUnit()
    {
        Assert.Equal(2000, CurrencyMinorUnits.ToMinorUnits(1999.5m, "JPY"));
    }

    [Theory]
    [InlineData("JPY", true)]
    [InlineData("jpy", true)]
    [InlineData("USD", false)]
    [InlineData("EUR", false)]
    [InlineData("XXX", false)]
    public void IsZeroDecimal_ClassifiesCurrencies(string currency, bool expected)
    {
        Assert.Equal(expected, CurrencyMinorUnits.IsZeroDecimal(currency));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToMinorUnits_BlankCurrency_Throws(string? currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => CurrencyMinorUnits.ToMinorUnits(10m, currency!));
    }

    [Fact]
    public void GenerateRefundIdempotencyKey_DistinguishesAmounts_SameAmountIsStable()
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var first = StripePaymentGateway.GenerateRefundIdempotencyKey(paymentId, orderId, 1999);
        var same = StripePaymentGateway.GenerateRefundIdempotencyKey(paymentId, orderId, 1999);
        var partial = StripePaymentGateway.GenerateRefundIdempotencyKey(paymentId, orderId, 500);

        Assert.Equal(first, same);
        Assert.NotEqual(first, partial);
    }
}
