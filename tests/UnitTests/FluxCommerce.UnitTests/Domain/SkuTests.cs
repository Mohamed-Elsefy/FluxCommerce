using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class SkuTests
{
    [Fact]
    public void Constructor_WithValidSku_Succeeds()
    {
        var sku = new Sku("SMARTPHONE-128GB");

        Assert.Equal("SMARTPHONE-128GB", sku.Value);
    }

    [Fact]
    public void Constructor_TrimsAndUppercases()
    {
        var sku = new Sku("  smartphone-128gb  ");

        Assert.Equal("SMARTPHONE-128GB", sku.Value);
    }

    [Fact]
    public void Constructor_AcceptsUnderscoresAndDots()
    {
        var sku = new Sku("PHONE_128.GB");

        Assert.Equal("PHONE_128.GB", sku.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithEmptyValue_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => new Sku(value));
    }

    [Theory]
    [InlineData("SMART PHONE")]
    [InlineData("SMART#PHONE")]
    [InlineData("SMART/PHONE")]
    [InlineData("SMARTPHONE-128!")]
    public void Constructor_WithInvalidCharacters_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => new Sku(value));
    }

    [Fact]
    public void Constructor_WithMaxLengthValue_Succeeds()
    {
        var value = string.Concat(Enumerable.Repeat("A", Sku.MaxLength));

        var sku = new Sku(value);

        Assert.Equal(value, sku.Value);
    }

    [Fact]
    public void Constructor_WithOverMaxLengthValue_Throws()
    {
        var value = string.Concat(Enumerable.Repeat("A", Sku.MaxLength + 1));

        Assert.Throws<ArgumentException>(() => new Sku(value));
    }

    [Fact]
    public void Equality_TwoSkusWithSameValue_AreEqual()
    {
        var first = new Sku("SMARTPHONE-128GB");
        var second = new Sku("smartphone-128gb");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoSkusWithDifferentValues_AreNotEqual()
    {
        var first = new Sku("SMARTPHONE-128GB");
        var second = new Sku("SMARTPHONE-256GB");

        Assert.NotEqual(first, second);
    }
}
