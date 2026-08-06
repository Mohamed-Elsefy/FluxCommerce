using FluxCommerce.Modules.Catalog.Application.Commands.ActivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.ArchiveProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- CreateProductCommandValidator ----

    [Fact]
    public void CreateProduct_ValidCommand_IsValid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            "Smartphone",
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateProduct_EmptyName_IsInvalid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            " ",
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateProduct_EmptySlug_IsInvalid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            "Smartphone",
            null,
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Slug"));
    }

    [Fact]
    public void CreateProduct_EmptyCategoryId_IsInvalid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            "Smartphone",
            "smartphone",
            null,
            Guid.Empty,
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("CategoryId"));
    }

    [Fact]
    public void CreateProduct_InvalidVisibility_IsInvalid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            "Smartphone",
            "smartphone",
            null,
            Guid.NewGuid(),
            (ProductVisibility)99));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Visibility"));
    }

    [Fact]
    public void CreateProduct_DescriptionOverMaxLength_IsInvalid()
    {
        var validator = new CreateProductCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductCommand(
            "Smartphone",
            "smartphone",
            string.Concat(Enumerable.Repeat("a", 2001)),
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Description"));
    }

    // ---- UpdateProductCommandValidator ----

    [Fact]
    public void UpdateProduct_ValidCommand_IsValid()
    {
        var validator = new UpdateProductCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductCommand(
            Guid.NewGuid(),
            "Smartphone",
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateProduct_EmptyId_IsInvalid()
    {
        var validator = new UpdateProductCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductCommand(
            Guid.Empty,
            "Smartphone",
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    [Fact]
    public void UpdateProduct_EmptyName_IsInvalid()
    {
        var validator = new UpdateProductCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductCommand(
            Guid.NewGuid(),
            "",
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void UpdateProduct_NameOverMaxLength_IsInvalid()
    {
        var validator = new UpdateProductCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductCommand(
            Guid.NewGuid(),
            string.Concat(Enumerable.Repeat("a", 201)),
            "smartphone",
            null,
            Guid.NewGuid(),
            ProductVisibility.Visible));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    // ---- ActivateProductCommandValidator ----

    [Fact]
    public void ActivateProduct_ValidId_IsValid()
    {
        var validator = new ActivateProductCommandValidator(Localizer);

        var result = validator.Validate(new ActivateProductCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ActivateProduct_EmptyId_IsInvalid()
    {
        var validator = new ActivateProductCommandValidator(Localizer);

        var result = validator.Validate(new ActivateProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    // ---- DeactivateProductCommandValidator ----

    [Fact]
    public void DeactivateProduct_ValidId_IsValid()
    {
        var validator = new DeactivateProductCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateProductCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeactivateProduct_EmptyId_IsInvalid()
    {
        var validator = new DeactivateProductCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- ArchiveProductCommandValidator ----

    [Fact]
    public void ArchiveProduct_ValidId_IsValid()
    {
        var validator = new ArchiveProductCommandValidator(Localizer);

        var result = validator.Validate(new ArchiveProductCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ArchiveProduct_EmptyId_IsInvalid()
    {
        var validator = new ArchiveProductCommandValidator(Localizer);

        var result = validator.Validate(new ArchiveProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- SoftDeleteProductCommandValidator ----

    [Fact]
    public void SoftDeleteProduct_ValidId_IsValid()
    {
        var validator = new SoftDeleteProductCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteProductCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SoftDeleteProduct_EmptyId_IsInvalid()
    {
        var validator = new SoftDeleteProductCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- RestoreProductCommandValidator ----

    [Fact]
    public void RestoreProduct_ValidId_IsValid()
    {
        var validator = new RestoreProductCommandValidator(Localizer);

        var result = validator.Validate(new RestoreProductCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RestoreProduct_EmptyId_IsInvalid()
    {
        var validator = new RestoreProductCommandValidator(Localizer);

        var result = validator.Validate(new RestoreProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }
}
