using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class InventorySkuTests
{
    [Fact]
    public void Constructor_WithValidSku_Succeeds()
    {
        var sku = new InventorySku("SKU-123");

        Assert.Equal("SKU-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithLowercaseSku_NormalizesToUppercase()
    {
        var sku = new InventorySku("sku-123");

        Assert.Equal("SKU-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithSurroundingWhitespace_Trims()
    {
        var sku = new InventorySku("  SKU-123  ");

        Assert.Equal("SKU-123", sku.Value);
    }

    [Fact]
    public void Constructor_WithNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventorySku(null));
    }

    [Fact]
    public void Constructor_WithBlank_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventorySku("   "));
    }

    [Fact]
    public void Constructor_WithInvalidCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventorySku("SKU 123!"));
    }

    [Fact]
    public void Constructor_WithLengthOverMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventorySku(new string('A', InventorySku.MaxLength + 1)));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var first = new InventorySku("SKU-1");
        var second = new InventorySku("sku-1");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(new InventorySku("SKU-1"), new InventorySku("SKU-2"));
    }
}
