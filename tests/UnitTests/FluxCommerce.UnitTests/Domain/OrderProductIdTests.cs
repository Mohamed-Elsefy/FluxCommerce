using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderProductIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var value = Guid.NewGuid();

        var productId = new OrderProductId(value);

        Assert.Equal(value, productId.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderProductId(Guid.Empty));
    }

    [Fact]
    public void Create_ReturnsNonEmptyIdentifier()
    {
        var productId = OrderProductId.Create();

        Assert.NotEqual(Guid.Empty, productId.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new OrderProductId(value);
        var second = new OrderProductId(value);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(OrderProductId.Create(), OrderProductId.Create());
    }
}
