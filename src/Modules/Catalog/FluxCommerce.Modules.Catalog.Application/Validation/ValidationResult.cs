namespace FluxCommerce.Modules.Catalog.Application.Validation;

public sealed class ValidationResult
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public bool IsValid { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private ValidationResult(bool isValid, IReadOnlyDictionary<string, string[]> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public static ValidationResult Success() => new(true, NoErrors);

    public static ValidationResult Failure(Dictionary<string, string[]> errors) => new(false, errors);
}
