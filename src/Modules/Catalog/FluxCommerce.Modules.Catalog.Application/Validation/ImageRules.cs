using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Validation;

public static class ImageRules
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

    public static void ValidateUrl(
        string? url,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (url.Trim().Length > ProductImage.ImageUrlMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, ProductImage.ImageUrlMaxLength));
    }

    public static void ValidateAltText(
        string? altText,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(altText))
            return;

        if (altText.Trim().Length > ProductImage.AltTextMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, ProductImage.AltTextMaxLength));
    }

    public static void ValidateDisplayOrder(
        int displayOrder,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (displayOrder < 0)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));
    }

    public static void ValidateOrderedImageIds(
        IReadOnlyCollection<Guid>? orderedImageIds,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (orderedImageIds is null || orderedImageIds.Count == 0)
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (orderedImageIds.Any(id => id == Guid.Empty))
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));

        if (orderedImageIds.Distinct().Count() != orderedImageIds.Count)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));
    }
}
