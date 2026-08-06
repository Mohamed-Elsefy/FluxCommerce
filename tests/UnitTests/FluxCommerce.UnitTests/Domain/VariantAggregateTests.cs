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
            UtcNow);

        Assert.Equal(ProductId, variant.ProductId);
        Assert.Equal("SMARTPHONE-128GB", variant.Sku.Value);
        Assert.Equal("128GB", variant.Name);
        Assert.Equal(VariantStatus.Active, variant.Status);
        Assert.False(variant.IsDeleted);
        Assert.Equal(UtcNow, variant.CreatedAtUtc);
        Assert.True(variant.GetDomainEvents().OfType<VariantCreated>().Any());
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.Empty, ProductId, new Sku("SKU-1"), "Name", UtcNow));
    }

    [Fact]
    public void Create_WithEmptyProductId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), Guid.Empty, new Sku("SKU-1"), "Name", UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), name!, UtcNow));
    }

    [Fact]
    public void Create_WithNameOverMaxLength_Throws()
    {
        var name = string.Concat(Enumerable.Repeat("a", Variant.NameMaxLength + 1));

        Assert.Throws<ArgumentException>(() =>
            Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), name, UtcNow));
    }

    // ---- Mutations ----

    [Fact]
    public void Rename_ChangesName()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

        var result = variant.Rename("New Name");

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", variant.Name);
    }

    [Fact]
    public void Rename_WithSameName_IsNoOp()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

        var result = variant.Rename("  Name  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Name", variant.Name);
    }

    [Fact]
    public void ChangeSku_UpdatesSku_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

        var result = variant.ChangeSku(new Sku("SKU-2"));

        Assert.True(result.IsSuccess);
        Assert.Equal("SKU-2", variant.Sku.Value);
        Assert.True(variant.GetDomainEvents().OfType<VariantSkuChanged>().Any());
    }

    [Fact]
    public void ChangeSku_ToSameSku_IsNoOp()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

        var result = variant.ChangeSku(new Sku("sku-1"));

        Assert.True(result.IsSuccess);
        Assert.Empty(variant.GetDomainEvents().OfType<VariantSkuChanged>());
    }

    // ---- Lifecycle ----

    [Fact]
    public void Activate_ActivatesInactiveVariant_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);
        variant.Deactivate();

        var result = variant.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(VariantStatus.Active, variant.Status);
        Assert.True(variant.GetDomainEvents().OfType<VariantActivated>().Any());
    }

    [Fact]
    public void Deactivate_DeactivatesActiveVariant_AndRaisesEvent()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

        var result = variant.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(VariantStatus.Inactive, variant.Status);
        Assert.True(variant.GetDomainEvents().OfType<VariantDeactivated>().Any());
    }

    [Fact]
    public void SoftDelete_MarksDeleted_AndRestore_Unmarks()
    {
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);

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
        var variant = Variant.Create(Guid.NewGuid(), ProductId, new Sku("SKU-1"), "Name", UtcNow);
        variant.SoftDelete(UtcNow);

        Assert.True(variant.Rename("Other").IsFailure);
        Assert.True(variant.ChangeSku(new Sku("SKU-2")).IsFailure);
        Assert.True(variant.Activate().IsFailure);
        Assert.True(variant.Deactivate().IsFailure);
        Assert.Equal(VariantErrors.DeletedVariantCannotBeModified, variant.Rename("Other").Error);
    }
}
