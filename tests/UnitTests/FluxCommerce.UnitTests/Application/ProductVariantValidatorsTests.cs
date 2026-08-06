using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductVariantValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- CreateProductVariantCommandValidator ----

    [Fact]
    public void CreateVariant_ValidCommand_IsValid()
    {
        var validator = new CreateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantCommand(Guid.NewGuid(), "SKU-1", "128GB"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateVariant_EmptyProductId_IsInvalid()
    {
        var validator = new CreateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantCommand(Guid.Empty, "SKU-1", "128GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void CreateVariant_EmptySku_IsInvalid()
    {
        var validator = new CreateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantCommand(Guid.NewGuid(), null, "128GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Sku"));
    }

    [Fact]
    public void CreateVariant_SkuOverMaxLength_IsInvalid()
    {
        var validator = new CreateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantCommand(Guid.NewGuid(), string.Concat(Enumerable.Repeat("A", 65)), "128GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Sku"));
    }

    [Fact]
    public void CreateVariant_EmptyName_IsInvalid()
    {
        var validator = new CreateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantCommand(Guid.NewGuid(), "SKU-1", " "));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    // ---- CreateProductVariantsCommandValidator ----

    [Fact]
    public void CreateVariants_ValidCommand_IsValid()
    {
        var validator = new CreateProductVariantsCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB"),
            new CreateProductVariantItemCommand("SKU-2", "256GB"),
        }));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateVariants_EmptyProductId_IsInvalid()
    {
        var validator = new CreateProductVariantsCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantsCommand(Guid.Empty, new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB"),
        }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void CreateVariants_EmptyItemSku_IsInvalid()
    {
        var validator = new CreateProductVariantsCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("", "128GB"),
        }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Variants[0].Sku"));
    }

    [Fact]
    public void CreateVariants_EmptyItemName_IsInvalid()
    {
        var validator = new CreateProductVariantsCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("SKU-1", ""),
        }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Variants[0].Name"));
    }

    [Fact]
    public void CreateVariants_MultipleInvalidItems_CollectsAllErrors()
    {
        var validator = new CreateProductVariantsCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("", ""),
            new CreateProductVariantItemCommand("", null),
        }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Variants[0].Sku"));
        Assert.True(result.Errors.ContainsKey("Variants[0].Name"));
        Assert.True(result.Errors.ContainsKey("Variants[1].Sku"));
        Assert.True(result.Errors.ContainsKey("Variants[1].Name"));
    }

    // ---- UpdateProductVariantCommandValidator ----

    [Fact]
    public void UpdateVariant_ValidCommand_IsValid()
    {
        var validator = new UpdateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductVariantCommand(Guid.NewGuid(), "SKU-1", "128GB"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateVariant_EmptyId_IsInvalid()
    {
        var validator = new UpdateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductVariantCommand(Guid.Empty, "SKU-1", "128GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    [Fact]
    public void UpdateVariant_EmptyName_IsInvalid()
    {
        var validator = new UpdateProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductVariantCommand(Guid.NewGuid(), "SKU-1", ""));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    // ---- DeleteProductVariantCommandValidator ----

    [Fact]
    public void DeleteVariant_ValidId_IsValid()
    {
        var validator = new DeleteProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductVariantCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeleteVariant_EmptyId_IsInvalid()
    {
        var validator = new DeleteProductVariantCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductVariantCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }
}
