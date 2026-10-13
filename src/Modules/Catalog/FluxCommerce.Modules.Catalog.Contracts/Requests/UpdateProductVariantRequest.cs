using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateProductVariantRequest(
    string? Sku,
    string? Name,
    decimal? UnitPriceAmount = null,
    string? Currency = null,
    VariantStatus? Status = null);
