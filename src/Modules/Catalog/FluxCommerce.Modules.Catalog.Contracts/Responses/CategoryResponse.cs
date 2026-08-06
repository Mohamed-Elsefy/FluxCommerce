using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentId,
    CategoryStatus Status,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc);
