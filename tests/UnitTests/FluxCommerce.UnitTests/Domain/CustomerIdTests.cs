using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CustomerIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var value = Guid.NewGuid();

        var customerId = new CustomerId(value);

        Assert.Equal(value, customerId.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CustomerId(Guid.Empty));
    }

    [Fact]
    public void Create_ReturnsNonEmptyIdentifier()
    {
        var customerId = CustomerId.Create();

        Assert.NotEqual(Guid.Empty, customerId.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new CustomerId(value);
        var second = new CustomerId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(CustomerId.Create(), CustomerId.Create());
    }
}
