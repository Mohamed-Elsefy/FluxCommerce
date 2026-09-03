namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateCategoryRequest(
    string? Name,
    string? Slug,
    string? Description,
    Guid? ParentId);
