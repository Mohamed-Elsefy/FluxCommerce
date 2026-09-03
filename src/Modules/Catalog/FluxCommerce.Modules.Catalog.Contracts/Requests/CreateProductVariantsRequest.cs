namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record CreateProductVariantsRequest(
    IReadOnlyCollection<CreateProductVariantRequest>? Variants);
