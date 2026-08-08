using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var id = new CartId(Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CartId(Guid.Empty));
    }

    [Fact]
    public void Create_GeneratesNonEmptyIdentifier()
    {
        var id = CartId.Create();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Create_GeneratesUniqueIdentifiers()
    {
        var first = CartId.Create();
        var second = CartId.Create();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_TwoIdsWithSameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new CartId(value);
        var second = new CartId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoIdsWithDifferentValues_AreNotEqual()
    {
        var first = new CartId(Guid.NewGuid());
        var second = new CartId(Guid.NewGuid());

        Assert.NotEqual(first, second);
    }
}
