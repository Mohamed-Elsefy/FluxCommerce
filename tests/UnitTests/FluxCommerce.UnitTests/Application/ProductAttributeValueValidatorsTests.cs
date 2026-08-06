using FluxCommerce.Modules.Catalog.Application.Commands.AssignProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.RemoveProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductAttributeValue;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductAttributeValueValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- AssignProductAttributeValueCommandValidator ----

    [Fact]
    public void AssignValue_ValidCommand_IsValid()
    {
        var validator = new AssignProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new AssignProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), "8GB"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssignValue_EmptyProductId_IsInvalid()
    {
        var validator = new AssignProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new AssignProductAttributeValueCommand(Guid.Empty, Guid.NewGuid(), "8GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void AssignValue_EmptyAttributeDefinitionId_IsInvalid()
    {
        var validator = new AssignProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new AssignProductAttributeValueCommand(Guid.NewGuid(), Guid.Empty, "8GB"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("AttributeDefinitionId"));
    }

    [Fact]
    public void AssignValue_EmptyValue_IsInvalid()
    {
        var validator = new AssignProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new AssignProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), ""));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    [Fact]
    public void AssignValue_ValueOverMaxLength_IsInvalid()
    {
        var validator = new AssignProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new AssignProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), string.Concat(Enumerable.Repeat("a", 501))));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    // ---- UpdateProductAttributeValueCommandValidator ----

    [Fact]
    public void UpdateValue_ValidCommand_IsValid()
    {
        var validator = new UpdateProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), "16GB"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateValue_EmptyValue_IsInvalid()
    {
        var validator = new UpdateProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid(), " "));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Value"));
    }

    // ---- RemoveProductAttributeValueCommandValidator ----

    [Fact]
    public void RemoveValue_ValidCommand_IsValid()
    {
        var validator = new RemoveProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new RemoveProductAttributeValueCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RemoveValue_EmptyAttributeDefinitionId_IsInvalid()
    {
        var validator = new RemoveProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new RemoveProductAttributeValueCommand(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("AttributeDefinitionId"));
    }

    [Fact]
    public void RemoveValue_EmptyProductId_IsInvalid()
    {
        var validator = new RemoveProductAttributeValueCommandValidator(Localizer);

        var result = validator.Validate(new RemoveProductAttributeValueCommand(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }
}
