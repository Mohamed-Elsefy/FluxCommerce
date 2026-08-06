using FluxCommerce.Modules.Catalog.Application.Validation;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ValidationRulesTests
{
    private readonly FakeLocalizer _localizer = new();

    private static Dictionary<string, string[]> NewErrors() => new();

    // ---- CategoryRules ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CategoryRules_ValidateName_Empty_AddsRequired(string? name)
    {
        var errors = NewErrors();

        CategoryRules.ValidateName(name, "Name", "Name", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Name"]);
    }

    [Fact]
    public void CategoryRules_ValidateName_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        CategoryRules.ValidateName(new string('a', Category.NameMaxLength + 1), "Name", "Name", errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Name"]);
    }

    [Fact]
    public void CategoryRules_ValidateName_Valid_NoErrors()
    {
        var errors = NewErrors();

        CategoryRules.ValidateName("Electronics", "Name", "Name", errors, _localizer);

        Assert.Empty(errors);
    }

    [Fact]
    public void CategoryRules_ValidateSlug_Empty_AddsRequired()
    {
        var errors = NewErrors();

        CategoryRules.ValidateSlug(null, errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Slug"]);
    }

    [Fact]
    public void CategoryRules_ValidateDescription_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        CategoryRules.ValidateDescription(new string('d', Category.DescriptionMaxLength + 1), "Description", "Description", errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Description"]);
    }

    // ---- ProductRules ----

    [Fact]
    public void ProductRules_ValidateId_Empty_AddsRequired()
    {
        var errors = NewErrors();

        ProductRules.ValidateId(Guid.Empty, "Id", "Id", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Id"]);
    }

    [Fact]
    public void ProductRules_ValidateId_NotEmpty_NoErrors()
    {
        var errors = NewErrors();

        ProductRules.ValidateId(Guid.NewGuid(), "Id", "Id", errors, _localizer);

        Assert.Empty(errors);
    }

    [Fact]
    public void ProductRules_ValidateSlug_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        ProductRules.ValidateSlug(new string('s', 256), errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Slug"]);
    }

    [Fact]
    public void ProductRules_ValidateVisibility_Undefined_AddsInvalid()
    {
        var errors = NewErrors();

        ProductRules.ValidateVisibility((ProductVisibility)99, "Visibility", "Visibility", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["Visibility"]);
    }

    [Fact]
    public void ProductRules_ValidateVisibility_Valid_NoErrors()
    {
        var errors = NewErrors();

        ProductRules.ValidateVisibility(ProductVisibility.Visible, "Visibility", "Visibility", errors, _localizer);

        Assert.Empty(errors);
    }

    // ---- AttributeRules ----

    [Fact]
    public void AttributeRules_ValidateDataType_Undefined_AddsInvalid()
    {
        var errors = NewErrors();

        AttributeRules.ValidateDataType((AttributeDataType)99, "DataType", "DataType", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["DataType"]);
    }

    [Fact]
    public void AttributeRules_ValidateSortOrder_Negative_AddsInvalid()
    {
        var errors = NewErrors();

        AttributeRules.ValidateSortOrder(-1, "SortOrder", "SortOrder", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["SortOrder"]);
    }

    [Fact]
    public void AttributeRules_ValidateValue_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        AttributeRules.ValidateValue(new string('v', ProductAttributeValue.ValueMaxLength + 1), "Value", "Value", errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Value"]);
    }

    // ---- VariantRules ----

    [Fact]
    public void VariantRules_ValidateId_Empty_AddsRequired()
    {
        var errors = NewErrors();

        VariantRules.ValidateId(Guid.Empty, "Id", "Id", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Id"]);
    }

    [Fact]
    public void VariantRules_ValidateName_Empty_AddsRequired()
    {
        var errors = NewErrors();

        VariantRules.ValidateName(" ", "Name", "Name", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Name"]);
    }

    [Fact]
    public void VariantRules_ValidateSku_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        VariantRules.ValidateSku(new string('s', 65), errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Sku"]);
    }

    [Fact]
    public void VariantRules_ValidateSku_Empty_AddsRequired()
    {
        var errors = NewErrors();

        VariantRules.ValidateSku("", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Sku"]);
    }

    // ---- SpecificationRules ----

    [Fact]
    public void SpecificationRules_ValidateId_Empty_AddsRequired()
    {
        var errors = NewErrors();

        SpecificationRules.ValidateId(Guid.Empty, "Id", "Id", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Id"]);
    }

    [Fact]
    public void SpecificationRules_ValidateName_Empty_AddsRequired()
    {
        var errors = NewErrors();

        SpecificationRules.ValidateName(null, "Name", "Name", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Name"]);
    }

    [Fact]
    public void SpecificationRules_ValidateValue_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        SpecificationRules.ValidateValue(new string('v', ProductSpecification.ValueMaxLength + 1), "Value", "Value", errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["Value"]);
    }

    // ---- ImageRules ----

    [Fact]
    public void ImageRules_ValidateId_Empty_AddsRequired()
    {
        var errors = NewErrors();

        ImageRules.ValidateId(Guid.Empty, "Id", "Id", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["Id"]);
    }

    [Fact]
    public void ImageRules_ValidateUrl_Empty_AddsRequired()
    {
        var errors = NewErrors();

        ImageRules.ValidateUrl("  ", "ImageUrl", "ImageUrl", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["ImageUrl"]);
    }

    [Fact]
    public void ImageRules_ValidateAltText_Empty_NoErrors()
    {
        var errors = NewErrors();

        ImageRules.ValidateAltText(null, "AltText", "AltText", errors, _localizer);

        Assert.Empty(errors);
    }

    [Fact]
    public void ImageRules_ValidateAltText_TooLong_AddsMaxLength()
    {
        var errors = NewErrors();

        ImageRules.ValidateAltText(new string('a', ProductImage.AltTextMaxLength + 1), "AltText", "AltText", errors, _localizer);

        Assert.Contains("Validation.Field.MaxLength", errors["AltText"]);
    }

    [Fact]
    public void ImageRules_ValidateDisplayOrder_Negative_AddsInvalid()
    {
        var errors = NewErrors();

        ImageRules.ValidateDisplayOrder(-1, "DisplayOrder", "DisplayOrder", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["DisplayOrder"]);
    }

    [Fact]
    public void ImageRules_ValidateOrderedImageIds_Null_AddsRequired()
    {
        var errors = NewErrors();

        ImageRules.ValidateOrderedImageIds(null, "OrderedImageIds", "OrderedImageIds", errors, _localizer);

        Assert.Contains("Validation.Field.Required", errors["OrderedImageIds"]);
    }

    [Fact]
    public void ImageRules_ValidateOrderedImageIds_EmptyGuid_AddsInvalid()
    {
        var errors = NewErrors();

        ImageRules.ValidateOrderedImageIds(new[] { Guid.NewGuid(), Guid.Empty }, "OrderedImageIds", "OrderedImageIds", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["OrderedImageIds"]);
    }

    [Fact]
    public void ImageRules_ValidateOrderedImageIds_Duplicates_AddsInvalid()
    {
        var id = Guid.NewGuid();
        var errors = NewErrors();

        ImageRules.ValidateOrderedImageIds(new[] { id, id }, "OrderedImageIds", "OrderedImageIds", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["OrderedImageIds"]);
    }

    [Fact]
    public void ImageRules_ValidateOrderedImageIds_Valid_NoErrors()
    {
        var errors = NewErrors();

        ImageRules.ValidateOrderedImageIds(new[] { Guid.NewGuid(), Guid.NewGuid() }, "OrderedImageIds", "OrderedImageIds", errors, _localizer);

        Assert.Empty(errors);
    }

    // ---- PaginationRules ----

    [Fact]
    public void PaginationRules_ValidatePageNumber_Zero_AddsInvalid()
    {
        var errors = NewErrors();

        PaginationRules.ValidatePageNumber(0, "PageNumber", "PageNumber", errors, _localizer);

        Assert.Contains("Validation.Field.Invalid", errors["PageNumber"]);
    }

    [Fact]
    public void PaginationRules_ValidatePageNumber_Null_NoErrors()
    {
        var errors = NewErrors();

        PaginationRules.ValidatePageNumber(null, "PageNumber", "PageNumber", errors, _localizer);

        Assert.Empty(errors);
    }

    [Fact]
    public void PaginationRules_ValidatePageSize_TooLarge_AddsRange()
    {
        var errors = NewErrors();

        PaginationRules.ValidatePageSize(101, "PageSize", "PageSize", errors, _localizer);

        Assert.Contains("Validation.Field.Range", errors["PageSize"]);
    }

    [Fact]
    public void PaginationRules_ValidatePageSize_Zero_AddsRange()
    {
        var errors = NewErrors();

        PaginationRules.ValidatePageSize(0, "PageSize", "PageSize", errors, _localizer);

        Assert.Contains("Validation.Field.Range", errors["PageSize"]);
    }

    [Fact]
    public void PaginationRules_ValidatePageSize_Valid_NoErrors()
    {
        var errors = NewErrors();

        PaginationRules.ValidatePageSize(20, "PageSize", "PageSize", errors, _localizer);

        Assert.Empty(errors);
    }
}
