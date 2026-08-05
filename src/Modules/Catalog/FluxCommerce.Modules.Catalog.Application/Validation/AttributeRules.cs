using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Validation;

public static class AttributeRules
{
    public static void ValidateId(
        Guid id,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (id == Guid.Empty)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
    }

    public static void ValidateName(
        string? name,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (name.Trim().Length > AttributeDefinition.NameMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, AttributeDefinition.NameMaxLength));
    }

    public static void ValidateSlug(string? slug, Dictionary<string, string[]> errors, ILocalizer localizer)
    {
        var displayName = localizer["Validation.Fields.Slug"];

        if (string.IsNullOrWhiteSpace(slug))
        {
            ValidationErrors.Add(errors, "Slug", localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (slug.Trim().Length > Slug.MaxLength)
            ValidationErrors.Add(errors, "Slug", localizer.Get("Validation.Field.MaxLength", displayName, Slug.MaxLength));
    }

    public static void ValidateDescription(
        string? description,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(description))
            return;

        if (description.Trim().Length > AttributeDefinition.DescriptionMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, AttributeDefinition.DescriptionMaxLength));
    }

    public static void ValidateDataType(
        AttributeDataType dataType,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (!Enum.IsDefined(typeof(AttributeDataType), dataType))
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));
    }

    public static void ValidateValue(
        string? value,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (value.Trim().Length > ProductAttributeValue.ValueMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, ProductAttributeValue.ValueMaxLength));
    }
}
