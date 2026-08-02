namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class NameRules
{
    public static void Validate(
        string? name,
        string propertyName,
        string displayName,
        int maxLength,
        Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationErrors.Add(errors, propertyName, $"{displayName} is required.");
            return;
        }

        if (name.Trim().Length > maxLength)
            ValidationErrors.Add(errors, propertyName, $"{displayName} must not exceed {maxLength} characters.");
    }
}
