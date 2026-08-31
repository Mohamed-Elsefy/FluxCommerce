using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.Results;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Commands;

/// <summary>
/// Shared orchestration for authenticated Cart item mutations:
///
///     ICurrentUser â”€â”€â–¶ resolve authenticated owner â”€â”€â–¶ resolve the user's Cart
///     â”€â”€â–¶ validate persisted ownership â”€â”€â–¶ hydrate the aggregate
///     â”€â”€â–¶ invoke the Domain behavior â”€â”€â–¶ persist through ICartStore with optimistic concurrency
///
/// Ownership is validated BEFORE any Domain mutation, from the persisted Cart state, never from a
/// request identifier or a client-supplied UserId. An authenticated operation never resolves a Guest
/// Cart, never mutates another user's Cart, never creates a Cart implicitly, and never persists a
/// failed mutation.
/// </summary>
internal static class AuthenticatedCartMutationExecutor
{
    public static async Task<Result<CartState>> ExecuteAsync(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        Func<CartAggregate, DateTimeOffset, DomainResult> mutate,
        CancellationToken cancellationToken = default)
    {
        var owner = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure<CartState>(owner.Error);
        }

        var state = await cartStore.GetByUserIdAsync(owner.Value.UserId!.Value, cancellationToken).ConfigureAwait(false);

        if (state is null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartNotFound.Code, CartErrors.CartNotFound.Description));
        }

        // The user index resolves the Cart, but the persisted ownership is validated anyway: an
        // authenticated path must never resolve a Guest Cart or another user's Cart.
        var ownership = AuthenticatedCartOwnership.ValidateOwnership(state, currentUser);

        if (ownership.IsFailure)
        {
            return Result.Failure<CartState>(ownership.Error);
        }

        var cart = CartApplicationMapper.ToAggregate(state);

        var utcNow = ToUtcOffset(dateTimeProvider.UtcNow);

        var result = mutate(cart, utcNow);

        if (result.IsFailure)
        {
            return Result.Failure<CartState>(CartErrorMapper.ToApplicationError(result.Error));
        }

        var save = await cartStore.SaveAsync(
            CartApplicationMapper.ToState(cart),
            expectedVersion: state.Version,
            cancellationToken).ConfigureAwait(false);

        return save.Status switch
        {
            CartSaveStatus.Success => Result.Success(save.Cart ?? CartApplicationMapper.ToState(cart)),
            CartSaveStatus.VersionConflict => Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description)),
            _ => Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartNotFound.Code, CartErrors.CartNotFound.Description)),
        };
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

