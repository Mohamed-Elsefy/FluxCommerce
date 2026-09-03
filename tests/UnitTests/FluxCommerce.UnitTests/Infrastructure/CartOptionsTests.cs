using System.ComponentModel.DataAnnotations;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CartOptionsTests
{
    private static IList<ValidationResult> Validate(CartOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }

    [Fact]
    public void Defaults_AreValid()
    {
        var options = new CartOptions();

        Assert.Equal("cart", options.KeyNamespace);
        Assert.Equal(7 * 24 * 60, options.DefaultTtlMinutes);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void CustomTtl_IsValid()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = 30 };

        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(525600)]
    public void BoundaryTtl_IsValid(int defaultTtlMinutes)
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = defaultTtlMinutes };

        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(525601)]
    public void OutOfRangeTtl_IsInvalid(int defaultTtlMinutes)
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = defaultTtlMinutes };

        Assert.Contains(Validate(options), r => r.MemberNames.Contains(nameof(CartOptions.DefaultTtlMinutes)));
    }

    [Fact]
    public void CustomNamespace_IsValid()
    {
        var options = new CartOptions { KeyNamespace = "custom-cart" };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void NullNamespace_IsInvalid()
    {
        var options = new CartOptions { KeyNamespace = null! };

        Assert.Contains(Validate(options), r => r.MemberNames.Contains(nameof(CartOptions.KeyNamespace)));
    }

    [Fact]
    public void EmptyNamespace_IsInvalid()
    {
        var options = new CartOptions { KeyNamespace = string.Empty };

        Assert.Contains(Validate(options), r => r.MemberNames.Contains(nameof(CartOptions.KeyNamespace)));
    }

    [Fact]
    public void WhitespaceNamespace_IsInvalid()
    {
        var options = new CartOptions { KeyNamespace = "   " };

        Assert.Contains(Validate(options), r => r.MemberNames.Contains(nameof(CartOptions.KeyNamespace)));
    }

    [Theory]
    [InlineData("cart items")]
    [InlineData("cart:items")]
    [InlineData("cart.items")]
    public void UnsupportedNamespaceCharacters_AreInvalid(string keyNamespace)
    {
        var options = new CartOptions { KeyNamespace = keyNamespace };

        Assert.Contains(Validate(options), r => r.MemberNames.Contains(nameof(CartOptions.KeyNamespace)));
    }
}
