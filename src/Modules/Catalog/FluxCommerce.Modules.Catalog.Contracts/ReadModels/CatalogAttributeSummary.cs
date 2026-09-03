using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.ReadModels;

public sealed record CatalogAttributeSummary(
    Guid AttributeDefinitionId,
    string Name,
    string Slug,
    AttributeDataType DataType,
    string Value);
