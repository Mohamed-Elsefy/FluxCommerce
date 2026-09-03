using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class ProductIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var id = new ProductId(Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ProductId(Guid.Empty));
    }

    [Fact]
    public void Create_GeneratesNonEmptyIdentifier()
    {
        var id = ProductId.Create();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Equality_TwoIdsWithSameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new ProductId(value);
        var second = new ProductId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoIdsWithDifferentValues_AreNotEqual()
    {
        Assert.NotEqual(new ProductId(Guid.NewGuid()), new ProductId(Guid.NewGuid()));
    }
}
