using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class VariantIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var id = new VariantId(Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new VariantId(Guid.Empty));
    }

    [Fact]
    public void Create_GeneratesNonEmptyIdentifier()
    {
        var id = VariantId.Create();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Equality_TwoIdsWithSameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new VariantId(value);
        var second = new VariantId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoIdsWithDifferentValues_AreNotEqual()
    {
        Assert.NotEqual(new VariantId(Guid.NewGuid()), new VariantId(Guid.NewGuid()));
    }
}
