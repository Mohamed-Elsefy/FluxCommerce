using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderSkuTests
{
    [Fact]
    public void Constructor_WithValidSku_Succeeds()
    {
        var sku = new OrderSku("ABC-123");

        Assert.Equal("ABC-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithLowercaseSku_NormalizesToUppercase()
    {
        var sku = new OrderSku("abc-123");

        Assert.Equal("ABC-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithSurroundingWhitespace_Trims()
    {
        var sku = new OrderSku("  ABC-123  ");

        Assert.Equal("ABC-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderSku(null));
    }

    [Fact]
    public void Constructor_WithBlank_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderSku("   "));
    }

    [Fact]
    public void Constructor_WithInvalidCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderSku("ABC 123!"));
    }

    [Fact]
    public void Constructor_WithLengthOverMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OrderSku(new string('A', OrderSku.MaxLength + 1)));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var first = new OrderSku("ABC-1");
        var second = new OrderSku("abc-1");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(new OrderSku("ABC-1"), new OrderSku("ABC-2"));
    }
}
