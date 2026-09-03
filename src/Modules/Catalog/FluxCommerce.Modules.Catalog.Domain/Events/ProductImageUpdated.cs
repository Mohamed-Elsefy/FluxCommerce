using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductImageUpdated : DomainEvent
{
    public Guid ProductId { get; }

    public Guid ImageId { get; }

    public string ImageUrl { get; }

    public string? AltText { get; }

    public int DisplayOrder { get; }

    public ProductImageUpdated(Guid productId, Guid imageId, string imageUrl, string? altText, int displayOrder)
    {
        ProductId = productId;
        ImageId = imageId;
        ImageUrl = imageUrl;
        AltText = altText;
        DisplayOrder = displayOrder;
    }
}
