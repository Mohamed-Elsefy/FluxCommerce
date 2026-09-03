namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record AssignProductAttributeValueRequest(
    Guid AttributeDefinitionId,
    string? Value);
