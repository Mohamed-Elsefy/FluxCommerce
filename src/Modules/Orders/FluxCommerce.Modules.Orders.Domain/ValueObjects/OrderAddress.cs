using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.ValueObjects;

public sealed class OrderAddress : ValueObject
{
    public const int RecipientNameMaxLength = 100;
    public const int Line1MaxLength = 200;
    public const int Line2MaxLength = 200;
    public const int CityMaxLength = 100;
    public const int RegionMaxLength = 100;
    public const int PostalCodeMaxLength = 20;
    public const int CountryCodeLength = 2;

    public string RecipientName { get; }

    public string Line1 { get; }

    public string? Line2 { get; }

    public string City { get; }

    public string Region { get; }

    public string PostalCode { get; }

    public string CountryCode { get; }

    public OrderAddress(
        string? recipientName,
        string? line1,
        string? line2,
        string? city,
        string? region,
        string? postalCode,
        string? countryCode)
    {
        RecipientName = ValidateRequired(recipientName, nameof(recipientName), RecipientNameMaxLength);
        Line1 = ValidateRequired(line1, nameof(line1), Line1MaxLength);
        Line2 = ValidateOptional(line2, nameof(line2), Line2MaxLength);
        City = ValidateRequired(city, nameof(city), CityMaxLength);
        Region = ValidateRequired(region, nameof(region), RegionMaxLength);
        PostalCode = ValidateRequired(postalCode, nameof(postalCode), PostalCodeMaxLength);
        CountryCode = ValidateCountryCode(countryCode);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return RecipientName;
        yield return Line1;
        yield return Line2 ?? string.Empty;
        yield return City;
        yield return Region;
        yield return PostalCode;
        yield return CountryCode;
    }

    private static string ValidateRequired(string? value, string paramName, int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException($"A {paramName} is required.", paramName);

        if (normalized.Length > maxLength)
            throw new ArgumentException($"{paramName} must not exceed {maxLength} characters.", paramName);

        return normalized;
    }

    private static string? ValidateOptional(string? value, string paramName, int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
            return null;

        if (normalized.Length > maxLength)
            throw new ArgumentException($"{paramName} must not exceed {maxLength} characters.", paramName);

        return normalized;
    }

    private static string ValidateCountryCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (normalized is null || normalized.Length != CountryCodeLength || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException(
                "A valid two-letter ISO 3166-1 country code is required.",
                nameof(value));

        return normalized;
    }
}
