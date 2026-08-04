namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public sealed record DomainError
{
    public string Code { get; }

    public string Description { get; }

    private DomainError(string code, string description, bool isSentinel = false)
    {
        if (!isSentinel)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("An error code is required.", nameof(code));

            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("An error description is required.", nameof(description));
        }

        Code = code;
        Description = description;
    }

    public static DomainError Create(string code, string description) => new(code, description);

    public static readonly DomainError None = new(string.Empty, string.Empty, isSentinel: true);
}
