namespace FluxCommerce.Modules.Cart.Contracts.Responses;

/// <summary>
/// Represents a Cart item in API responses.
/// </summary>
public sealed record CartItemResponse(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string ProductName,
    decimal UnitPriceAmount,
    string Currency,
    int Quantity);

/// <summary>
/// Represents a Cart in API responses.
/// </summary>
public sealed record CartResponse(
    Guid CartId,
    Guid? UserId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<CartItemResponse> Items,
    bool IsGuest,
    bool IsAuthenticated);