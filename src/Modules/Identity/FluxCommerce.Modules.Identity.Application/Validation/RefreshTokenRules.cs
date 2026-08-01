using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class RefreshTokenRules
{
    public const int MaxLength = 2048;

    public static void Validate(string? refreshToken, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            ValidationErrors.Add(errors, "RefreshToken", "RefreshToken is required.");
            return;
        }

        if (refreshToken.Length > MaxLength)
            ValidationErrors.Add(errors, "RefreshToken", $"RefreshToken must not exceed {MaxLength} characters.");
    }
}
