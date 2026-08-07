namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed record CartItemDocument(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity);
