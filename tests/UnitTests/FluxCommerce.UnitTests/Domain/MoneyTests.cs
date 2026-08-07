using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Constructor_WithValidAmountAndCurrency_Succeeds()
    {
        var money = new Money(19.99m, "USD");

        Assert.Equal(19.99m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Constructor_WithZeroAmount_Succeeds()
    {
        var money = new Money(0m, "USD");

        Assert.Equal(0m, money.Amount);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m, "USD"));
    }

    [Fact]
    public void Constructor_WithNullCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, null));
    }

    [Fact]
    public void Constructor_WithBlankCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, "  "));
    }

    [Fact]
    public void Constructor_WithLowercaseCurrency_NormalizesToUppercase()
    {
        var money = new Money(10m, "usd");

        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Constructor_WithShortCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, "US"));
    }

    [Fact]
    public void Constructor_WithLongCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, "USDD"));
    }

    [Fact]
    public void Constructor_WithNonAlphaCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, "US1"));
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var first = new Money(5.5m, "EUR");
        var second = new Money(5.5m, "EUR");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentAmount_AreNotEqual()
    {
        Assert.NotEqual(new Money(5.5m, "EUR"), new Money(5.6m, "EUR"));
    }

    [Fact]
    public void Equality_DifferentCurrency_AreNotEqual()
    {
        Assert.NotEqual(new Money(5.5m, "EUR"), new Money(5.5m, "USD"));
    }
}
