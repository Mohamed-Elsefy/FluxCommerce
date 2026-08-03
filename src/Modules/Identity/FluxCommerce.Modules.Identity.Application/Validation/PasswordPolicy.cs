using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const int MaxLength = 128;

    private const string SpecialCharacters = "!@#$%^&*()-_=+[]{};:'\",.<>/?\\|`~";

    public static void Validate(string? password, Dictionary<string, string[]> errors, ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Required"]);
            return;
        }

        if (password.Length < MinLength)
            ValidationErrors.Add(errors, "Password", localizer.Get("Validation.Password.MinLength", MinLength));

        if (password.Length > MaxLength)
            ValidationErrors.Add(errors, "Password", localizer.Get("Validation.Password.MaxLength", MaxLength));

        if (!password.Any(char.IsUpper))
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Uppercase"]);

        if (!password.Any(char.IsLower))
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Lowercase"]);

        if (!password.Any(char.IsDigit))
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Digit"]);

        if (!password.Any(SpecialCharacters.Contains))
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Special"]);
    }
}
