namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateProductSpecificationRequest(
    string? Name,
    string? Value);
