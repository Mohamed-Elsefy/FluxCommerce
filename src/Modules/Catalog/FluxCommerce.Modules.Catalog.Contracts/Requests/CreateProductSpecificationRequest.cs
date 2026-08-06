namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record CreateProductSpecificationRequest(
    string? Name,
    string? Value);
