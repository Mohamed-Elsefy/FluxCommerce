namespace FluxCommerce.Modules.Catalog.Application.Validation;

internal static class ValidationErrors
{
    public static void Add(Dictionary<string, string[]> errors, string property, string message)
    {
        errors[property] = errors.TryGetValue(property, out var existing)
            ? [.. existing, message]
            : [message];
    }
}
