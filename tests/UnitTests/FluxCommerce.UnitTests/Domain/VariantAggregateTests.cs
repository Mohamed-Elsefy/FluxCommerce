using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class VariantAggregateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ProductId = Guid.NewGuid();

    // ---- Creation ----

    [Fact]
    public void Create_CreatesActiveVariant_AndRaisesEvent()
    {
        var variant = Variant.Create(
            Guid.NewGuid(),
            ProductId,
            new Sku("SMARTPHONE-128GB"),
            "128GB",
            new Money(1299.00m, "USD"),
            UtcNow);

        Assert.Equal(ProductId, variant.ProductId);
        Assert.Equal("SMARTPHONE-128GB", variant.Sku.Value);
        Assert.Equal("128GB", variant.Name);
        Assert.Equal(1299.00m, variant.UnitPrice.Amount);
        Assert.Equal("USD", variant.UnitPrice.Currency);
        Assert.Equal(VariantStatus.Active, variant.Status);
        Assert.False(variant.IsDeleted);
        Assert.Equal(UtcNow, variant.CreatedAtUtc);
        Assert.True(variant.GetDomainEvents().OfType<VariantCreated>().Any());
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.Empty, ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow));
    }

    [Fact]
    public void Create_WithEmptyProductId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), Guid.Empty, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), name!, new Money(1299.00m, "USD"), UtcNow));
    }

    [Fact]
    public void Create_WithNameOverMaxLength_Throws()
    {
        var name = string.Concat(Enumerable.Repeat("a", Variant.NameMaxLength + 1));

        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), name, new Money(1299.00m, "USD"), UtcNow));
    }

    // ---- Mutations ----

    [Fact]
    public void SetUnitPrice_UpdatesPrice()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.SetUnitPrice(new Money(1199.00m, "USD"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1199.00m, variant.UnitPrice.Amount);
        Assert.Equal("USD", variant.UnitPrice.Currency);
    }

    [Fact]
    public void SetUnitPrice_WithSamePrice_IsNoOp()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.SetUnitPrice(new Money(1299.00m, "USD"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1299.00m, variant.UnitPrice.Amount);
    }

    [Fact]
    public void SetUnitPrice_WithNullPrice_Throws()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        Assert.Throws<ArgumentNullException>(() => variant.SetUnitPrice(null!));
    }

    [Fact]
    public void Rename_ChangesName()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.Rename("New Name");

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", variant.Name);
    }

    [Fact]
    public void Rename_WithSameName_IsNoOp()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.Rename("  Name  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Name", variant.Name);
    }

    [Fact]
    public void ChangeSku_UpdatesSku_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.ChangeSku(new Sku("SKU-2"));

        Assert.True(result.IsSuccess);
        Assert.Equal("SKU-2", variant.Sku.Value);
        Assert.True(variant.GetDomainEvents().OfType<VariantSkuChanged>().Any());
    }

    [Fact]
    public void ChangeSku_ToSameSku_IsNoOp()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.ChangeSku(new Sku("sku-1"));

        Assert.True(result.IsSuccess);
        Assert.Empty(variant.GetDomainEvents().OfType<VariantSkuChanged>());
    }

    // ---- Lifecycle ----

    [Fact]
    public void Activate_ActivatesInactiveVariant_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);
        variant.Deactivate();

        var result = variant.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(VariantStatus.Active, variant.Status);
        Assert.True(variant.GetDomainEvents().OfType<VariantActivated>().Any());
    }

    [Fact]
    public void Deactivate_DeactivatesActiveVariant_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var result = variant.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(VariantStatus.Inactive, variant.Status);
        Assert.True(variant.GetDomainEvents().OfType<VariantDeactivated>().Any());
    }

    [Fact]
    public void SoftDelete_MarksDeleted_AndRestore_Unmarks()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);

        var deleteResult = variant.SoftDelete(UtcNow);
        Assert.True(deleteResult.IsSuccess);
        Assert.True(variant.IsDeleted);
        Assert.Equal(UtcNow, variant.DeletedAtUtc);

        var restoreResult = variant.Restore();
        Assert.True(restoreResult.IsSuccess);
        Assert.False(variant.IsDeleted);
        Assert.Null(variant.DeletedAtUtc);
    }

    [Fact]
    public void DeletedVariant_CannotBeModified()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", new Money(1299.00m, "USD"), UtcNow);
        variant.SoftDelete(UtcNow);

        Assert.True(variant.Rename("Other").IsFailure);
        Assert.True(variant.ChangeSku(new Sku("SKU-2")).IsFailure);
        Assert.True(variant.SetUnitPrice(new Money(9.99m, "USD")).IsFailure);
        Assert.True(variant.Activate().IsFailure);
        Assert.True(variant.Deactivate().IsFailure);
        Assert.Equal(VariantErrors.DeletedVariantCannotBeModified, variant.Rename("Other").Error);
    }
}
