using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class SkuTests
{
    [Fact]
    public void Constructor_WithValidSku_Succeeds()
    {
        var sku = new Sku("SHIRT-BLUE-M");

        Assert.Equal("SHIRT-BLUE-M", sku.Value);
    }

    [Fact]
    public void Constructor_WithNullSku_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sku(null));
    }

    [Fact]
    public void Constructor_WithBlankSku_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sku("   "));
    }

    [Fact]
    public void Constructor_WithLowercaseSku_NormalizesToUppercase()
    {
        var sku = new Sku("shirt-blue-m");

        Assert.Equal("SHIRT-BLUE-M", sku.Value);
    }

    [Fact]
    public void Constructor_WithLeadingAndTrailingWhitespace_Trims()
    {
        var sku = new Sku("  SHIRT  ");

        Assert.Equal("SHIRT", sku.Value);
    }

    [Fact]
    public void Constructor_WithSkuExceedingMaxLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sku(new string('A', Sku.MaxLength + 1)));
    }

    [Theory]
    [InlineData("SHIRT@")]
    [InlineData("SHIRT BLUE")]
    [InlineData("SHIRT#M")]
    public void Constructor_WithInvalidCharacters_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => new Sku(value));
    }

    [Fact]
    public void Equality_TwoSkusWithSameValue_AreEqual()
    {
        Assert.Equal(new Sku("SHIRT"), new Sku("shirt"));
    }

    [Fact]
    public void Equality_TwoSkusWithDifferentValues_AreNotEqual()
    {
        Assert.NotEqual(new Sku("SHIRT"), new Sku("PANTS"));
    }
}
