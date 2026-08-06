namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogCategoryTree(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentId,
    IReadOnlyCollection<CatalogCategoryTree> Children);
