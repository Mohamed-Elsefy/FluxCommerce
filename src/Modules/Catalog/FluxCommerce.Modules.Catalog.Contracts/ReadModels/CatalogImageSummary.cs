namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogImageSummary(
    Guid Id,
    string ImageUrl,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary);
