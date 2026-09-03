using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class SeoMetadataTests
{
    [Fact]
    public void Create_WithAllValues_Succeeds()
    {
        var seo = SeoMetadata.Create("iPhone 14", "Latest smartphone", ["phone", "apple"]);

        Assert.Equal("iPhone 14", seo.Title);
        Assert.Equal("Latest smartphone", seo.Description);
        Assert.Equal(["phone", "apple"], seo.Keywords);
    }

    [Fact]
    public void Create_TrimsValues()
    {
        var seo = SeoMetadata.Create("  iPhone 14  ", "  Latest smartphone  ", [" phone ", " apple "]);

        Assert.Equal("iPhone 14", seo.Title);
        Assert.Equal("Latest smartphone", seo.Description);
        Assert.Equal(["phone", "apple"], seo.Keywords);
    }

    [Fact]
    public void Create_WithEmptyTitleAndDescription_NormalizesToNull()
    {
        var seo = SeoMetadata.Create("", "   ", null);

        Assert.Null(seo.Title);
        Assert.Null(seo.Description);
        Assert.Empty(seo.Keywords);
    }

    [Fact]
    public void Create_DeduplicatesKeywords_CaseInsensitively_AndDropsEmpty()
    {
        var seo = SeoMetadata.Create("Title", "Description", ["Phone", "phone", "", "  ", "Apple"]);

        Assert.Equal(["Phone", "Apple"], seo.Keywords);
    }

    [Fact]
    public void Create_WithTitleOverMaxLength_Throws()
    {
        var title = string.Concat(Enumerable.Repeat("a", SeoMetadata.MaxTitleLength + 1));

        Assert.Throws<ArgumentException>(() => SeoMetadata.Create(title, null, null));
    }

    [Fact]
    public void Create_WithDescriptionOverMaxLength_Throws()
    {
        var description = string.Concat(Enumerable.Repeat("a", SeoMetadata.MaxDescriptionLength + 1));

        Assert.Throws<ArgumentException>(() => SeoMetadata.Create(null, description, null));
    }

    [Fact]
    public void Create_WithTooManyKeywords_Throws()
    {
        var keywords = Enumerable.Range(0, SeoMetadata.MaxKeywordsCount + 1).Select(i => $"keyword{i}");

        Assert.Throws<ArgumentException>(() => SeoMetadata.Create("Title", "Description", keywords));
    }

    [Fact]
    public void Create_WithKeywordOverMaxLength_Throws()
    {
        var keyword = string.Concat(Enumerable.Repeat("a", SeoMetadata.MaxKeywordLength + 1));

        Assert.Throws<ArgumentException>(() => SeoMetadata.Create("Title", "Description", [keyword]));
    }

    [Fact]
    public void Create_WithTitleAtMaxLength_Succeeds()
    {
        var title = string.Concat(Enumerable.Repeat("a", SeoMetadata.MaxTitleLength));

        var seo = SeoMetadata.Create(title, null, null);

        Assert.Equal(title, seo.Title);
    }

    [Fact]
    public void Equality_TwoSeoMetadataWithSameValues_AreEqual()
    {
        var first = SeoMetadata.Create("Title", "Description", ["a", "b"]);
        var second = SeoMetadata.Create("Title", "Description", ["a", "b"]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_TwoSeoMetadataWithDifferentValues_AreNotEqual()
    {
        var first = SeoMetadata.Create("Title", "Description", ["a"]);
        var second = SeoMetadata.Create("Title", "Description", ["b"]);

        Assert.NotEqual(first, second);
    }
}
