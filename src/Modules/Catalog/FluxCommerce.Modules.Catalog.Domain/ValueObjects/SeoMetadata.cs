using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.ValueObjects;

public sealed class SeoMetadata : ValueObject
{
    public const int MaxTitleLength = 200;

    public const int MaxDescriptionLength = 500;

    public const int MaxKeywordsCount = 10;

    public const int MaxKeywordLength = 50;

    public string? Title { get; }

    public string? Description { get; }

    public IReadOnlyList<string> Keywords { get; }

    private SeoMetadata(string? title, string? description, IEnumerable<string>? keywords)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

        if (normalizedTitle is { Length: > MaxTitleLength })
            throw new ArgumentException($"Title must not exceed {MaxTitleLength} characters.", nameof(title));

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (normalizedDescription is { Length: > MaxDescriptionLength })
            throw new ArgumentException($"Description must not exceed {MaxDescriptionLength} characters.", nameof(description));

        var normalizedKeywords = keywords?
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (normalizedKeywords.Length > MaxKeywordsCount)
            throw new ArgumentException($"Keywords must not exceed {MaxKeywordsCount} entries.", nameof(keywords));

        if (normalizedKeywords.Any(keyword => keyword.Length > MaxKeywordLength))
            throw new ArgumentException($"Each keyword must not exceed {MaxKeywordLength} characters.", nameof(keywords));

        Title = normalizedTitle;
        Description = normalizedDescription;
        Keywords = normalizedKeywords;
    }

    public static SeoMetadata Create(string? title, string? description, IEnumerable<string>? keywords = null) =>
        new(title, description, keywords);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Title ?? string.Empty;
        yield return Description ?? string.Empty;

        foreach (var keyword in Keywords)
            yield return keyword;
    }
}
