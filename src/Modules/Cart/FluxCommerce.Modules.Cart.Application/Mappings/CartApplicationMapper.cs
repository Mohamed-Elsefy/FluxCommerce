using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Mappings;

public static class CartApplicationMapper
{
    public static CartState ToState(CartAggregate cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return new CartState(
            cart.Id,
            cart.UserId,
            cart.Version,
            cart.CreatedAtUtc,
            cart.UpdatedAtUtc,
            cart.Items.Select(ToItemState).ToList());
    }

    public static CartAggregate ToAggregate(CartState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var owner = state.UserId is Guid userId ? CartOwner.Authenticated(userId) : CartOwner.Guest();

        return CartAggregate.Hydrate(
            state.CartId,
            owner,
            state.Version,
            state.CreatedAtUtc,
            state.UpdatedAtUtc,
            state.Items.Select(ToItem).ToList());
    }

    private static CartItemState ToItemState(CartItem item) =>
        new(
            item.ProductId,
            item.VariantId,
            item.Sku,
            item.ProductName,
            item.UnitPrice,
            item.Quantity);

    private static CartItem ToItem(CartItemState item) =>
        new(
            item.ProductId,
            item.VariantId,
            item.Sku,
            item.ProductName,
            item.UnitPrice,
            item.Quantity);
}

