using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Validation;

public static class ProductRules
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

        if (name.Trim().Length > Product.NameMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, Product.NameMaxLength));
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

        if (description.Trim().Length > Product.DescriptionMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, Product.DescriptionMaxLength));
    }

    public static void ValidateVisibility(
        ProductVisibility visibility,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (!Enum.IsDefined(typeof(ProductVisibility), visibility))
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));
    }
}
