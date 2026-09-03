using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Catalog.Domain.Events;

public sealed class ProductImageAdded : DomainEvent
{
    public Guid ProductId { get; }

    public Guid ImageId { get; }

    public string ImageUrl { get; }

    public bool IsPrimary { get; }

    public ProductImageAdded(Guid productId, Guid imageId, string imageUrl, bool isPrimary)
    {
        ProductId = productId;
        ImageId = imageId;
        ImageUrl = imageUrl;
        IsPrimary = isPrimary;
    }
}
