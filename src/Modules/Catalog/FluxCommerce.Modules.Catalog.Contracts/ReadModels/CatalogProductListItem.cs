namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogProductListItem(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid CategoryId,
    CatalogImageSummary? PrimaryImage);
