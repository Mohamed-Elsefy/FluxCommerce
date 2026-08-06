namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record ProductSpecificationResponse(
    Guid Id,
    Guid ProductId,
    string Name,
    string Value);
