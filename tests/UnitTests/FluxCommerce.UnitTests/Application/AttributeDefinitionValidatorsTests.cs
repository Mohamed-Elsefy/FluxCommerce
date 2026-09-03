using FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AttributeDefinitionValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- CreateAttributeDefinitionCommandValidator ----

    [Fact]
    public void CreateAttributeDefinition_ValidCommand_IsValid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            "Description",
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateAttributeDefinition_EmptyName_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateAttributeDefinition_NameOverMaxLength_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            string.Concat(Enumerable.Repeat("a", 101)),
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void CreateAttributeDefinition_EmptySlug_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "RAM",
            null,
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Slug"));
    }

    [Fact]
    public void CreateAttributeDefinition_InvalidDataType_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            (AttributeDataType)99,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("DataType"));
    }

    [Fact]
    public void CreateAttributeDefinition_NegativeSortOrder_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            -1,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("SortOrder"));
    }

    [Fact]
    public void CreateAttributeDefinition_DescriptionOverMaxLength_IsInvalid()
    {
        var validator = new CreateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new CreateAttributeDefinitionCommand(
            "RAM",
            "ram",
            string.Concat(Enumerable.Repeat("a", 501)),
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Description"));
    }

    // ---- UpdateAttributeDefinitionCommandValidator ----

    [Fact]
    public void UpdateAttributeDefinition_ValidCommand_IsValid()
    {
        var validator = new UpdateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new UpdateAttributeDefinitionCommand(
            Guid.NewGuid(),
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateAttributeDefinition_EmptyId_IsInvalid()
    {
        var validator = new UpdateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new UpdateAttributeDefinitionCommand(
            Guid.Empty,
            "RAM",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    [Fact]
    public void UpdateAttributeDefinition_EmptyName_IsInvalid()
    {
        var validator = new UpdateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new UpdateAttributeDefinitionCommand(
            Guid.NewGuid(),
            " ",
            "ram",
            null,
            AttributeDataType.Text,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Fact]
    public void UpdateAttributeDefinition_InvalidDataType_IsInvalid()
    {
        var validator = new UpdateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new UpdateAttributeDefinitionCommand(
            Guid.NewGuid(),
            "RAM",
            "ram",
            null,
            (AttributeDataType)99,
            false,
            false,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("DataType"));
    }

    // ---- ActivateAttributeDefinitionCommandValidator ----

    [Fact]
    public void ActivateAttributeDefinition_ValidId_IsValid()
    {
        var validator = new ActivateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new ActivateAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ActivateAttributeDefinition_EmptyId_IsInvalid()
    {
        var validator = new ActivateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new ActivateAttributeDefinitionCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Id"));
    }

    // ---- DeactivateAttributeDefinitionCommandValidator ----

    [Fact]
    public void DeactivateAttributeDefinition_ValidId_IsValid()
    {
        var validator = new DeactivateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeactivateAttributeDefinition_EmptyId_IsInvalid()
    {
        var validator = new DeactivateAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new DeactivateAttributeDefinitionCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- SoftDeleteAttributeDefinitionCommandValidator ----

    [Fact]
    public void SoftDeleteAttributeDefinition_ValidId_IsValid()
    {
        var validator = new SoftDeleteAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SoftDeleteAttributeDefinition_EmptyId_IsInvalid()
    {
        var validator = new SoftDeleteAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new SoftDeleteAttributeDefinitionCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }

    // ---- RestoreAttributeDefinitionCommandValidator ----

    [Fact]
    public void RestoreAttributeDefinition_ValidId_IsValid()
    {
        var validator = new RestoreAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new RestoreAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RestoreAttributeDefinition_EmptyId_IsInvalid()
    {
        var validator = new RestoreAttributeDefinitionCommandValidator(Localizer);

        var result = validator.Validate(new RestoreAttributeDefinitionCommand(Guid.Empty));

        Assert.False(result.IsValid);
    }
}
