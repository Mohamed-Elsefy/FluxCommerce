using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class GuestCartIdentifierTests
{
    [Fact]
    public void Generate_ProducesNonEmptyIdentifier()
    {
        var id = CartId.Create();

        Assert.NotNull(id);
        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Generate_ProducesUniqueIdentifiers_AcrossManyDraws()
    {
        var values = Enumerable.Range(0, 1000)
            .Select(_ => CartId.Create().Value)
            .ToArray();

        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void Generate_ProducesRandomRfc4122Version4Identifier()
    {
        var bytes = CartId.Create().Value.ToByteArray();

        // RFC 4122 layout: the version nibble (0100 = random) is the high nibble of byte 7,
        // and the variant nibble (10xx = RFC 4122) is the high two bits of byte 8. These
        // markers prove the identifier is fully random — i.e. non-sequential and hard to guess.
        Assert.Equal(4, bytes[7] >> 4);
        Assert.Equal(0b10, bytes[8] >> 6);
    }

    [Fact]
    public void Generate_IsNonSequential_BetweenConsecutiveDraws()
    {
        var draws = Enumerable.Range(0, 100)
            .Select(_ => CartId.Create().Value.ToByteArray())
            .ToArray();

        // A sequential scheme yields a strictly monotonic series. Random v4 identifiers
        // do not, so the draw series must contain at least one inversion.
        Assert.False(
            Enumerable.Range(0, draws.Length - 1)
                .All(index => IsLessThan(draws[index], draws[index + 1])));
    }

    [Fact]
    public void Generate_ContainsOnlyHexDigitsAndHyphens_NoEmbeddedPii()
    {
        var id = CartId.Create().Value.ToString("D");

        Assert.Equal(36, id.Length);
        Assert.All(
            id.Where(character => character != '-'),
            character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public void Generate_IsIndependentFromAuthenticatedUserId()
    {
        var userId = Guid.NewGuid();

        Assert.NotEqual(userId, CartId.Create().Value);
        Assert.NotEqual(userId.ToString("D"), CartId.Create().Value.ToString("D"));
    }

    [Fact]
    public void Generate_IsCompatibleWithCartKeyGenerator()
    {
        var cartId = CartId.Create();
        var generator = new CartKeyGenerator(Options.Create(new CartOptions { KeyNamespace = "cart" }));

        Assert.Equal($"cart:{cartId.Value:D}", generator.ForCart(cartId));
    }

    private static bool IsLessThan(byte[] first, byte[] second)
    {
        for (var index = 0; index < first.Length; index++)
        {
            if (first[index] != second[index])
                return first[index] < second[index];
        }

        return false;
    }
}
