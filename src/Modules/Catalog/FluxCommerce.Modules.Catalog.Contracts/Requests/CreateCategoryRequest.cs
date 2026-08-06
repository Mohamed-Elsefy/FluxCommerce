namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record CreateCategoryRequest(
    string? Name,
    string? Slug,
    string? Description,
    Guid? ParentId);
