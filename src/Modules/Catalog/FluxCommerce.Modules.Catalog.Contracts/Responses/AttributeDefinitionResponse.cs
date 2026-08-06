using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record AttributeDefinitionResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    AttributeDataType DataType,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    int SortOrder,
    Guid? CategoryId,
    AttributeStatus Status,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc);
