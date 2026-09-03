using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderVariantIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var value = Guid.NewGuid();

        var variantId = new OrderVariantId(value);

        Assert.Equal(value, variantId.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderVariantId(Guid.Empty));
    }

    [Fact]
    public void Create_ReturnsNonEmptyIdentifier()
    {
        var variantId = OrderVariantId.Create();

        Assert.NotEqual(Guid.Empty, variantId.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new OrderVariantId(value);
        var second = new OrderVariantId(value);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(OrderVariantId.Create(), OrderVariantId.Create());
    }
}
