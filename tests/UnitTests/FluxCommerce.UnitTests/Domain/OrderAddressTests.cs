using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderAddressTests
{
    private static OrderAddress CreateValidAddress() =>
        new("Jane Doe", "1 Main Street", null, "Springfield", "IL", "62701", "US");

    [Fact]
    public void Constructor_WithValidValues_Succeeds()
    {
        var address = CreateValidAddress();

        Assert.Equal("Jane Doe", address.RecipientName);
        Assert.Equal("1 Main Street", address.Line1);
        Assert.Equal("Springfield", address.City);
        Assert.Equal("IL", address.Region);
        Assert.Equal("62701", address.PostalCode);
        Assert.Equal("US", address.CountryCode);
    }

    [Fact]
    public void Constructor_WithOptionalLine2_NormalizesToNullWhenBlank()
    {
        var address = new OrderAddress("Jane Doe", "1 Main Street", "   ", "Springfield", "IL", "62701", "US");

        Assert.Null(address.Line2);
    }

    [Fact]
    public void Constructor_WithLowercaseCountryCode_NormalizesToUppercase()
    {
        var address = new OrderAddress("Jane Doe", "1 Main Street", null, "Springfield", "IL", "62701", "us");

        Assert.Equal("US", address.CountryCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingRecipientName_Throws(string? recipientName)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress(recipientName, "1 Main Street", null, "Springfield", "IL", "62701", "US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingLine1_Throws(string? line1)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", line1, null, "Springfield", "IL", "62701", "US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingCity_Throws(string? city)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", "1 Main Street", null, city, "IL", "62701", "US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingRegion_Throws(string? region)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", "1 Main Street", null, "Springfield", region, "62701", "US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingPostalCode_Throws(string? postalCode)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", "1 Main Street", null, "Springfield", "IL", postalCode, "US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("USA")]
    [InlineData("U")]
    [InlineData("US1")]
    public void Constructor_WithInvalidCountryCode_Throws(string? countryCode)
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", "1 Main Street", null, "Springfield", "IL", "62701", countryCode));
    }

    [Fact]
    public void Constructor_WithLine1OverMax_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderAddress("Jane Doe", new string('A', OrderAddress.Line1MaxLength + 1), null, "Springfield", "IL", "62701", "US"));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var first = CreateValidAddress();
        var second = CreateValidAddress();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var first = CreateValidAddress();
        var second = new OrderAddress("John Doe", "1 Main Street", null, "Springfield", "IL", "62701", "US");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_DifferentLine2_AreNotEqual()
    {
        var first = CreateValidAddress();
        var second = new OrderAddress("Jane Doe", "1 Main Street", "Apt 2", "Springfield", "IL", "62701", "US");

        Assert.NotEqual(first, second);
    }
}
