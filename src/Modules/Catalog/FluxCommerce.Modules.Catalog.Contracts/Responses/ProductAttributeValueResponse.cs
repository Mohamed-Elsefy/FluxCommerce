using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record ProductAttributeValueResponse(
    Guid Id,
    Guid ProductId,
    Guid AttributeDefinitionId,
    string AttributeName,
    string AttributeSlug,
    AttributeDataType DataType,
    string Value);
