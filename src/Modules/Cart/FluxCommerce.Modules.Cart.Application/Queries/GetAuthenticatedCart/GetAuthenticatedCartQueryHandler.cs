using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;

namespace FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;

public sealed class GetAuthenticatedCartQueryHandler
    : IQueryHandler<GetAuthenticatedCartQuery, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly ICurrentUser _currentUser;

    public GetAuthenticatedCartQueryHandler(ICartStore cartStore, ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<CartState>> HandleAsync(
        GetAuthenticatedCartQuery query,
        CancellationToken cancellationToken = default)
    {
        var owner = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(_currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure<CartState>(owner.Error);
        }

        var cart = await _cartStore.GetByUserIdAsync(owner.Value.UserId!.Value, cancellationToken).ConfigureAwait(false);

        if (cart is null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartNotFound.Code, CartErrors.CartNotFound.Description));
        }

        // Defense-in-depth: the user index resolves this Cart, but the persisted ownership is
        // validated anyway so a corrupted index can never surface a Guest or foreign Cart.
        var ownership = AuthenticatedCartOwnership.ValidateOwnership(cart, _currentUser);

        if (ownership.IsFailure)
        {
            return Result.Failure<CartState>(ownership.Error);
        }

        return Result.Success(cart);
    }
}
