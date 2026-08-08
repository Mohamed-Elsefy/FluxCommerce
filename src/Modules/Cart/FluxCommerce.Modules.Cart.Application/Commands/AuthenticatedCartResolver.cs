using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Commands;

/// <summary>
/// Shared get-or-create orchestration for the authenticated Cart: resolves the current
/// authenticated owner, resolves the user's active Cart through the user index (validating
/// persisted ownership), or creates the Cart atomically when the user has none yet. This is the
/// single shared implementation of the authenticated Cart creation behavior used by the
/// authenticated Cart create handler and by the Guest → Authenticated merge when the target Cart
/// does not exist yet.
///
///     ICurrentUser ──▶ resolve authenticated owner ──▶ user index lookup
///         ├─ existing ──▶ validate ownership ──▶ return the active Cart
///         └─ missing ──▶ CreateAuthenticated ──▶ CreateForUserAsync (atomic one-active-Cart)
///             └─ concurrent winner ──▶ re-read winner through the user index
/// </summary>
internal static class AuthenticatedCartResolver
{
    public static async Task<Result<CartState>> ResolveOrCreateAsync(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        var owner = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure<CartState>(owner.Error);
        }

        var userId = owner.Value.UserId!.Value;

        // One active Cart per user: a repeated create returns the existing active Cart unchanged.
        var existing = await cartStore.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            var ownership = AuthenticatedCartOwnership.ValidateOwnership(existing, currentUser);

            if (ownership.IsFailure)
            {
                return Result.Failure<CartState>(ownership.Error);
            }

            return Result.Success(existing);
        }

        var utcNow = ToUtcOffset(dateTimeProvider.UtcNow);

        var cart = CartAggregate.CreateAuthenticated(CartId.Create(), userId, utcNow);

        var state = CartApplicationMapper.ToState(cart);

        // Atomic create: cart key + user index key are written together only when the user has no
        // active Cart yet, so a concurrent create cannot produce a second active Cart.
        var save = await cartStore.CreateForUserAsync(state, cancellationToken).ConfigureAwait(false);

        return save.Status switch
        {
            CartSaveStatus.Success => Result.Success(save.Cart ?? state),
            CartSaveStatus.DuplicateActiveCart => await ResolveConcurrentWinnerAsync(cartStore, userId, cancellationToken).ConfigureAwait(false),
            _ => Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description)),
        };
    }

    private static async Task<Result<CartState>> ResolveConcurrentWinnerAsync(
        ICartStore cartStore,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var winner = await cartStore.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return winner is not null
            ? Result.Success(winner)
            : Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description));
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
