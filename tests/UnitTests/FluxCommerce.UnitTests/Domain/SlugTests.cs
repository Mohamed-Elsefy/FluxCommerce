using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class SlugTests
{
    [Fact]
    public void Constructor_WithValidSlug_Succeeds()
    {
        var slug = new Slug("electronics");

        Assert.Equal("electronics", slug.Value);
    }

    [Fact]
    public void Constructor_TrimsAndLowercases()
    {
        var slug = new Slug("  Electronics  ");

        Assert.Equal("electronics", slug.Value);
    }

    [Fact]
    public void Constructor_AcceptsDigitsAndHyphens()
    {
        var slug = new Slug("iphone-14-pro-128gb");

        Assert.Equal("iphone-14-pro-128gb", slug.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithEmptyValue_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => new Slug(value));
    }

    [Theory]
    [InlineData("my product")]
    [InlineData("my_product")]
    [InlineData("my.product")]
    [InlineData("café")]
    [InlineData("a b")]
    public void Constructor_WithInvalidCharacters_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => new Slug(value));
    }

    [Theory]
    [InlineData("-electronics")]
    [InlineData("electronics-")]
    [InlineData("my--phone")]
    public void Constructor_WithInvalidHyphens_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => new Slug(value));
    }

    [Fact]
    public void Constructor_WithMaxLengthValue_Succeeds()
    {
        var value = string.Concat(Enumerable.Repeat("a", Slug.MaxLength));

        var slug = new Slug(value);

        Assert.Equal(value, slug.Value);
    }

    [Fact]
    public void Constructor_WithOverMaxLengthValue_Throws()
    {
        var value = string.Concat(Enumerable.Repeat("a", Slug.MaxLength + 1));

        Assert.Throws<ArgumentException>(() => new Slug(value));
    }

    [Fact]
    public void Equality_TwoSlugsWithSameValue_AreEqual()
    {
        var first = new Slug("electronics");
        var second = new Slug("ELECTRONICS");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoSlugsWithDifferentValues_AreNotEqual()
    {
        var first = new Slug("electronics");
        var second = new Slug("apparel");

        Assert.NotEqual(first, second);
    }
}
