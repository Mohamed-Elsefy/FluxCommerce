using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class ProductImage : Entity<Guid>
{
    public const int ImageUrlMaxLength = 2048;

    public const int AltTextMaxLength = 500;

    public string ImageUrl { get; private set; }

    public string? AltText { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsPrimary { get; private set; }

    public Dictionary<string, string>? Metadata { get; private set; }

    public DateTime CreatedAtUtc { get; }

    internal ProductImage(
        Guid id,
        string imageUrl,
        string? altText,
        int displayOrder,
        bool isPrimary,
        Dictionary<string, string>? metadata,
        DateTime utcNow)
        : base(id)
    {
        ImageUrl = imageUrl;
        AltText = altText;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
        Metadata = metadata;
        CreatedAtUtc = utcNow;
    }

    #pragma warning disable CS8618
    private ProductImage() { }
    #pragma warning restore CS8618

    internal void ChangeDetails(string imageUrl, string? altText, int displayOrder, Dictionary<string, string>? metadata)
    {
        ImageUrl = imageUrl;
        AltText = altText;
        DisplayOrder = displayOrder;
        Metadata = metadata;
    }

    internal void SetAsPrimary()
    {
        IsPrimary = true;
    }

    internal void ClearPrimary()
    {
        IsPrimary = false;
    }

    internal void ChangeDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
    }
}
