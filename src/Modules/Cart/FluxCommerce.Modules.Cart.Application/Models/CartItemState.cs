using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Models;

public sealed record CartItemState(
    ProductId ProductId,
    VariantId VariantId,
    Sku Sku,
    string ProductName,
    Money UnitPrice,
    int Quantity);
