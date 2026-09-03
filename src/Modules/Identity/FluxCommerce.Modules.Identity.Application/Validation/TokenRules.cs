using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class TokenRules
{
    public const int MaxLength = 2048;

    public static void Validate(string? token, string propertyName, string displayName, Dictionary<string, string[]> errors, ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (token.Length > MaxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, MaxLength));
    }
}
