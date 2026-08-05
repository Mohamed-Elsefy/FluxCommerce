using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    Guid CategoryId,
    ProductVisibility Visibility,
    SeoRequest? Seo);
