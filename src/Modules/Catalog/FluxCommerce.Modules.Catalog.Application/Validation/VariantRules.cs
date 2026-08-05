using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Validation;

public static class VariantRules
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

        if (name.Trim().Length > Variant.NameMaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, Variant.NameMaxLength));
    }

    public static void ValidateSku(string? sku, Dictionary<string, string[]> errors, ILocalizer localizer) =>
        ValidateSku(sku, "Sku", errors, localizer);

    public static void ValidateSku(string? sku, string propertyName, Dictionary<string, string[]> errors, ILocalizer localizer)
    {
        var displayName = localizer["Validation.Fields.Sku"];

        if (string.IsNullOrWhiteSpace(sku))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (sku.Trim().Length > Sku.MaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, Sku.MaxLength));
    }
}
