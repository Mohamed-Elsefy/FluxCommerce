using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record CreateAttributeDefinitionRequest(
    string? Name,
    string? Slug,
    string? Description,
    AttributeDataType DataType,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    int SortOrder,
    Guid? CategoryId);
