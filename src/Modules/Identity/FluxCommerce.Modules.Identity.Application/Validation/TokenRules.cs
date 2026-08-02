using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class TokenRules
{
    public const int MaxLength = 2048;

    public static void Validate(string? token, string propertyName, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ValidationErrors.Add(errors, propertyName, $"{propertyName} is required.");
            return;
        }

        if (token.Length > MaxLength)
            ValidationErrors.Add(errors, propertyName, $"{propertyName} must not exceed {MaxLength} characters.");
    }
}
