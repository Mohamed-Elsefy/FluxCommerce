namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateProductVariantRequest(
    string? Sku,
    string? Name);
