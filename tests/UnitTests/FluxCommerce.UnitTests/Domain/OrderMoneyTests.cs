using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderMoneyTests
{
    [Fact]
    public void Constructor_WithValidAmountAndCurrency_Succeeds()
    {
        var money = new OrderMoney(19.99m, "USD");

        Assert.Equal(19.99m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Constructor_WithZeroAmount_Succeeds()
    {
        var money = new OrderMoney(0m, "USD");

        Assert.Equal(0m, money.Amount);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderMoney(-0.01m, "USD"));
    }

    [Fact]
    public void Constructor_WithNullCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderMoney(10m, null));
    }

    [Fact]
    public void Constructor_WithBlankCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderMoney(10m, "  "));
    }

    [Fact]
    public void Constructor_WithLowercaseCurrency_NormalizesToUppercase()
    {
        var money = new OrderMoney(10m, "usd");

        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Constructor_WithShortCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderMoney(10m, "US"));
    }

    [Fact]
    public void Constructor_WithLongCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderMoney(10m, "USDD"));
    }

    [Fact]
    public void Constructor_WithNonAlphaCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderMoney(10m, "US1"));
    }

    [Fact]
    public void Zero_ReturnsZeroAmountInCurrency()
    {
        var money = OrderMoney.Zero("EUR");

        Assert.Equal(0m, money.Amount);
        Assert.Equal("EUR", money.Currency);
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        var result = new OrderMoney(5.5m, "USD").Add(new OrderMoney(2.25m, "USD"));

        Assert.Equal(7.75m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        var money = new OrderMoney(5m, "USD");

        Assert.Throws<InvalidOperationException>(() => money.Add(new OrderMoney(5m, "EUR")));
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var result = new OrderMoney(5.5m, "USD").Subtract(new OrderMoney(2.25m, "USD"));

        Assert.Equal(3.25m, result.Amount);
    }

    [Fact]
    public void Subtract_ResultingInNegative_Throws()
    {
        var money = new OrderMoney(2m, "USD");

        Assert.Throws<InvalidOperationException>(() => money.Subtract(new OrderMoney(5m, "USD")));
    }

    [Fact]
    public void Multiply_PositiveFactor_ReturnsProduct()
    {
        var result = new OrderMoney(3m, "USD").Multiply(2.5m);

        Assert.Equal(7.5m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Multiply_NegativeFactor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderMoney(3m, "USD").Multiply(-1m));
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var first = new OrderMoney(5.5m, "EUR");
        var second = new OrderMoney(5.5m, "EUR");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentAmount_AreNotEqual()
    {
        Assert.NotEqual(new OrderMoney(5.5m, "EUR"), new OrderMoney(5.6m, "EUR"));
    }

    [Fact]
    public void Equality_DifferentCurrency_AreNotEqual()
    {
        Assert.NotEqual(new OrderMoney(5.5m, "EUR"), new OrderMoney(5.5m, "USD"));
    }
}
