using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Ownership;

/// <summary>
/// Shared authenticated Cart ownership boundary. This is the single place where an
/// authenticated identity is resolved from <see cref="ICurrentUser"/> and where a persisted
/// Cart's ownership is validated against the current user before any authenticated use case
/// mutates it.
///
///     ICurrentUser ──▶ ResolveAuthenticatedOwner ──▶ CartOwner (UserId)
///                                                          │
///     persisted CartState ◀── ValidateOwnership ────────────┘
///
/// Ownership is always validated from the persisted state, never assumed from a request
/// identifier. Guest Carts are never reachable through an authenticated path, and a UserId
/// must never be confused with a Guest Cart identifier, a CartId, or a Redis key.
/// </summary>
internal static class AuthenticatedCartOwnership
{
    /// <summary>
    /// Resolves the authenticated owner from <see cref="ICurrentUser"/>. Fails with
    /// <c>Cart.AuthenticationRequired</c> (Unauthorized) when the caller is not authenticated,
    /// has no UserId, or exposes an empty UserId. A Guest identity is never treated as an
    /// authenticated UserId.
    /// </summary>
    public static Result<CartOwner> ResolveAuthenticatedOwner(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId || userId == Guid.Empty)
        {
            return Result.Failure<CartOwner>(AuthenticationRequiredError());
        }

        return Result.Success(CartOwner.Authenticated(userId));
    }

    /// <summary>
    /// Validates that a persisted Cart belongs to the authenticated caller. Fails with
    /// <c>Cart.AuthenticationRequired</c> when the caller is not authenticated, and with
    /// <c>Cart.CartAccessDenied</c> when the Cart is a Guest Cart (authenticated paths never
    /// resolve Guest Carts) or belongs to a different user. This must be called before any
    /// Domain mutation so an ownership mismatch is rejected before mutation.
    /// </summary>
    public static Result ValidateOwnership(CartState cart, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var owner = ResolveAuthenticatedOwner(currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure(owner.Error);
        }

        // An authenticated access path must never resolve a Guest Cart: a Guest Cart is
        // reachable only through its opaque identifier, never through a UserId lookup.
        if (cart.UserId is null)
        {
            return Result.Failure(AccessDeniedError());
        }

        if (cart.UserId != owner.Value.UserId)
        {
            return Result.Failure(AccessDeniedError());
        }

        return Result.Success();
    }

    private static Error AuthenticationRequiredError() =>
        CartErrorMapper.ToApplicationError(CartErrors.AuthenticationRequired);

    private static Error AccessDeniedError() =>
        CartErrorMapper.ToApplicationError(CartErrors.CartAccessDenied);
}
