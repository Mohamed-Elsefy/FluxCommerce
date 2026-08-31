using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;

public sealed class GetGuestCartQueryHandler : IQueryHandler<GetGuestCartQuery, Result<CartState>>
{
    private readonly ICartStore _cartStore;

    public GetGuestCartQueryHandler(ICartStore cartStore)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
    }

    public async Task<Result<CartState>> HandleAsync(
        GetGuestCartQuery query,
        CancellationToken cancellationToken = default)
    {
        CartId cartId;
        try
        {
            cartId = new CartId(query.CartId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(
                Error.Validation(CartErrors.InvalidCartId.Code, CartErrors.InvalidCartId.Description));
        }

        var cart = await _cartStore.GetAsync(cartId, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartNotFound.Code, CartErrors.CartNotFound.Description));
        }

        // The identifier alone is not proof of guest ownership: ownership is persisted in the
        // cart state. An authenticated cart resolved through a Guest identifier is rejected.
        if (cart.UserId is not null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartAccessDenied.Code, CartErrors.CartAccessDenied.Description));
        }

        return Result.Success(cart);
    }
}

