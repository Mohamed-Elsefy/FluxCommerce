namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const int MaxLength = 128;

    private const string SpecialCharacters = "!@#$%^&*()-_=+[]{};:'\",.<>/?\\|`~";

    public static void Validate(string? password, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            ValidationErrors.Add(errors, "Password", "Password is required.");
            return;
        }

        if (password.Length < MinLength)
            ValidationErrors.Add(errors, "Password", $"Password must be at least {MinLength} characters long.");

        if (password.Length > MaxLength)
            ValidationErrors.Add(errors, "Password", $"Password must not exceed {MaxLength} characters.");

        if (!password.Any(char.IsUpper))
            ValidationErrors.Add(errors, "Password", "Password must contain at least one uppercase letter.");

        if (!password.Any(char.IsLower))
            ValidationErrors.Add(errors, "Password", "Password must contain at least one lowercase letter.");

        if (!password.Any(char.IsDigit))
            ValidationErrors.Add(errors, "Password", "Password must contain at least one digit.");

        if (!password.Any(SpecialCharacters.Contains))
            ValidationErrors.Add(errors, "Password", "Password must contain at least one special character.");
    }
}
