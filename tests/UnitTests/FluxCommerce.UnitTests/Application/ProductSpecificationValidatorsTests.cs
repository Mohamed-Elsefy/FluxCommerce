using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductSpecification;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductSpecificationValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- CreateProductSpecificationCommandValidator ----

    [Fact]
    public void CreateSpecification_ValidCommand_IsValid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.NewGuid(), "Color", "Black"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateSpecification_EmptyProductId_IsInvalid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.Empty, "Color", "Black"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void CreateSpecification_EmptyName_IsInvalid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.NewGuid(), " ", "Black"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateSpecification_NameOverMaxLength_IsInvalid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.NewGuid(), string.Concat(Enumerable.Repeat("a", 201)), "Black"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateSpecification_EmptyValue_IsInvalid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.NewGuid(), "Color", ""));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    [Fact]
    public void CreateSpecification_ValueOverMaxLength_IsInvalid()
    {
        var validator = new CreateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new CreateProductSpecificationCommand(Guid.NewGuid(), "Color", string.Concat(Enumerable.Repeat("a", 1001))));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    // ---- UpdateProductSpecificationCommandValidator ----

    [Fact]
    public void UpdateSpecification_ValidCommand_IsValid()
    {
        var validator = new UpdateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductSpecificationCommand(Guid.NewGuid(), Guid.NewGuid(), "Color", "White"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpecification_EmptySpecificationId_IsInvalid()
    {
        var validator = new UpdateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductSpecificationCommand(Guid.NewGuid(), Guid.Empty, "Color", "White"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("SpecificationId"));
    }

    [Fact]
    public void UpdateSpecification_EmptyName_IsInvalid()
    {
        var validator = new UpdateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductSpecificationCommand(Guid.NewGuid(), Guid.NewGuid(), "", "White"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void UpdateSpecification_EmptyValue_IsInvalid()
    {
        var validator = new UpdateProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductSpecificationCommand(Guid.NewGuid(), Guid.NewGuid(), "Color", null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    // ---- DeleteProductSpecificationCommandValidator ----

    [Fact]
    public void DeleteSpecification_ValidCommand_IsValid()
    {
        var validator = new DeleteProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductSpecificationCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeleteSpecification_EmptyProductId_IsInvalid()
    {
        var validator = new DeleteProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductSpecificationCommand(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void DeleteSpecification_EmptySpecificationId_IsInvalid()
    {
        var validator = new DeleteProductSpecificationCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductSpecificationCommand(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("SpecificationId"));
    }
}
