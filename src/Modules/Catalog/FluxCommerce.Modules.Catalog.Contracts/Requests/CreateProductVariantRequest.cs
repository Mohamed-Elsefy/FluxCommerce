namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record CreateProductVariantRequest(
    string? Sku,
    string? Name);
