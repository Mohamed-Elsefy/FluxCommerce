using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData(" user@example.com ", "user@example.com")]
    [InlineData("User@Example.COM", "user@example.com")]
    [InlineData("first.last+tag@sub.domain.co", "first.last+tag@sub.domain.co")]
    public void ValidEmail_IsAccepted(string value, string expected)
    {
        var email = new Email(value);

        Assert.Equal(expected, email.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@example.com")]
    public void InvalidEmail_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => new Email(value));
    }

    [Fact]
    public void Email_ExceedingMaxLength_Throws()
    {
        var value = $"{new string('a', 250)}@example.com";

        Assert.Throws<ArgumentException>(() => new Email(value));
    }

    [Fact]
    public void Email_IsNormalizedToLowerCase()
    {
        var email = new Email("  User@Example.COM ");

        Assert.Equal("user@example.com", email.Value);
    }

    [Fact]
    public void EqualEmails_AreValueEqual()
    {
        var left = new Email("user@example.com");
        var right = new Email("USER@EXAMPLE.COM");

        Assert.Equal(left, right);
    }
}
