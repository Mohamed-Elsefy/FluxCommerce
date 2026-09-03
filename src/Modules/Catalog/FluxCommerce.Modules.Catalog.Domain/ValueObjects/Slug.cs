using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.ValueObjects;

public sealed class Slug : ValueObject
{
    public const int MaxLength = 255;

    public string Value { get; }

    public Slug(string? value)
    {
        var slug = value?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("Slug is required.", nameof(value));

        if (slug.Length > MaxLength)
            throw new ArgumentException($"Slug must not exceed {MaxLength} characters.", nameof(value));

        if (!IsValidSlugFormat(slug))
            throw new ArgumentException(
                "Slug must contain only lowercase letters, digits and single hyphens, and must not start or end with a hyphen.",
                nameof(value));

        Value = slug;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool IsValidSlugFormat(string slug)
    {
        var previousWasHyphen = false;

        for (var i = 0; i < slug.Length; i++)
        {
            var character = slug[i];

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                previousWasHyphen = false;
                continue;
            }

            if (character == '-' && !previousWasHyphen && i > 0 && i < slug.Length - 1)
            {
                previousWasHyphen = true;
                continue;
            }

            return false;
        }

        return true;
    }
}
