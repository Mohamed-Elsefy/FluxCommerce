using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var value = Guid.NewGuid();

        var orderId = new OrderId(value);

        Assert.Equal(value, orderId.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderId(Guid.Empty));
    }

    [Fact]
    public void Create_ReturnsNonEmptyIdentifier()
    {
        var orderId = OrderId.Create();

        Assert.NotEqual(Guid.Empty, orderId.Value);
    }

    [Fact]
    public void Create_ReturnsUniqueIdentifiers()
    {
        var first = OrderId.Create();
        var second = OrderId.Create();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new OrderId(value);
        var second = new OrderId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(OrderId.Create(), OrderId.Create());
    }
}
