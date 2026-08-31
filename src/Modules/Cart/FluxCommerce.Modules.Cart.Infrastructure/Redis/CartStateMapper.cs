using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed class CartStateMapper
{
    public CartDocument ToDocument(CartState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new CartDocument(
            state.CartId.Value,
            state.UserId,
            state.Version,
            state.CreatedAtUtc,
            state.UpdatedAtUtc,
            state.Items.Select(ToItemDocument).ToList());
    }

    public CartState ToState(CartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new CartState(
            new CartId(document.CartId),
            document.UserId,
            document.Version,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Items.Select(ToItemState).ToList());
    }

    private static CartItemDocument ToItemDocument(CartItemState item) =>
        new(
            item.ProductId.Value,
            item.VariantId.Value,
            item.Sku.Value,
            item.ProductName,
            item.UnitPrice.Amount,
            item.UnitPrice.Currency,
            item.Quantity);

    private static CartItemState ToItemState(CartItemDocument item) =>
        new(
            new ProductId(item.ProductId),
            new VariantId(item.VariantId),
            new Sku(item.Sku),
            item.ProductName,
            new Money(item.UnitPrice, item.Currency),
            item.Quantity);
}

