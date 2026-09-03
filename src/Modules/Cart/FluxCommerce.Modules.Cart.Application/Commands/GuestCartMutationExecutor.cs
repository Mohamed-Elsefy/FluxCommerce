using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.Results;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Commands;

/// <summary>
/// Shared orchestration for Guest Cart item mutations:
///
///     validate identifier â†’ resolve cart â†’ validate Guest ownership â†’ hydrate the aggregate
///     â†’ invoke the Domain behavior â†’ persist through ICartStore with optimistic concurrency
///
/// Ownership is validated BEFORE any Domain mutation: the stored cart ownership is checked,
/// not assumed from the caller-supplied identifier. A Guest operation never touches an
/// authenticated Cart, never creates a Cart implicitly, and never persists a failed mutation.
/// </summary>
internal static class GuestCartMutationExecutor
{
    public static async Task<Result<CartState>> ExecuteAsync(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        Guid cartId,
        Func<CartAggregate, DateTimeOffset, DomainResult> mutate,
        CancellationToken cancellationToken = default)
    {
        CartId id;
        try
        {
            id = new CartId(cartId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(
                Error.Validation(CartErrors.InvalidCartId.Code, CartErrors.InvalidCartId.Description));
        }

        var state = await cartStore.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (state is null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartNotFound.Code, CartErrors.CartNotFound.Description));
        }

        // The identifier alone is not proof of Guest ownership: ownership is persisted in the
        // cart state. An authenticated cart resolved through a Guest identifier is rejected
        // before any Domain mutation or persistence occurs.
        if (state.UserId is not null)
        {
            return Result.Failure<CartState>(
                Error.NotFound(CartErrors.CartAccessDenied.Code, CartErrors.CartAccessDenied.Description));
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

