using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    SeoResponse? Seo,
    ProductStatus Status,
    ProductVisibility Visibility,
    Guid CategoryId,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc);
