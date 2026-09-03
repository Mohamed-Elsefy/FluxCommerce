using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderNumberTests
{
    [Fact]
    public void Constructor_WithValidNumber_Succeeds()
    {
        var orderNumber = new OrderNumber("ORD-123");

        Assert.Equal("ORD-123", orderNumber.Value);
    }

    [Fact]
    public void Constructor_WithLowercaseNumber_NormalizesToUppercase()
    {
        var orderNumber = new OrderNumber("ord-123");

        Assert.Equal("ORD-123", orderNumber.Value);
    }

    [Fact]
    public void Constructor_WithSurroundingWhitespace_Trims()
    {
        var orderNumber = new OrderNumber("  ORD-123  ");

        Assert.Equal("ORD-123", orderNumber.Value);
    }

    [Fact]
    public void Constructor_WithNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderNumber(null));
    }

    [Fact]
    public void Constructor_WithBlank_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderNumber("   "));
    }

    [Fact]
    public void Constructor_WithLengthOverMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderNumber(new string('A', OrderNumber.MaxLength + 1)));
    }

    [Fact]
    public void Create_ReturnsNonEmptyNumber()
    {
        var orderNumber = OrderNumber.Create();

        Assert.False(string.IsNullOrEmpty(orderNumber.Value));
    }

    [Fact]
    public void Create_ReturnsUniqueNumbers()
    {
        var first = OrderNumber.Create();
        var second = OrderNumber.Create();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var first = new OrderNumber("ORD-1");
        var second = new OrderNumber("ord-1");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(new OrderNumber("ORD-1"), new OrderNumber("ORD-2"));
    }
}
