using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class NameRules
{
    public static void Validate(
        string? name,
        string propertyName,
        string displayName,
        int maxLength,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (name.Trim().Length > maxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, maxLength));
    }
}
