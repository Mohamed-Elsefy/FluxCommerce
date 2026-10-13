using FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record VariantResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPriceAmount,
    string Currency,
    VariantStatus Status,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc);
