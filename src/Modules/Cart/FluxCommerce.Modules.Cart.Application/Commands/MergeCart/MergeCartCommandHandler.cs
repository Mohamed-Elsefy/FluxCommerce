using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Commands.MergeCart;

/// <summary>
/// Phase 3.6.2: Guest → Authenticated Cart merge with Guest Cart consumption.
///
///     ICurrentUser ──▶ resolve authenticated owner (the target User)
///     GuestCartId  ──▶ resolve Guest source Cart ──▶ validate Guest ownership
///     ──▶ resolve (or atomically create) the user's active target Cart
///     ──▶ hydrate both aggregates ──▶ Domain Cart.Merge ──▶ atomically persist the target
///          and consume the Guest source through ICartStore.MergeAndConsumeAsync
///
/// Ownership is validated from persisted state before any mutation: the source must be Guest-owned
/// (an authenticated Cart is never a valid source) and the target is always the current user's own
/// Cart. Add-missing, sum-duplicate and quantity-limit semantics come from the existing Domain
/// <c>Cart.Merge</c>. The target is persisted and the Guest source is consumed in one atomic
/// storage operation, so a failed merge never consumes the source and a retried merge can never
/// re-apply the Guest quantities. A retried merge of an already-consumed Guest Cart resolves the
/// source as missing and returns CartNotFound without mutating the target.
/// </summary>
public sealed class MergeCartCommandHandler : ICommandHandler<MergeCartCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;

    public MergeCartCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<CartState>> HandleAsync(
        MergeCartCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. The authenticated target owner comes exclusively from ICurrentUser.
        var owner = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(_currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure<CartState>(owner.Error);
        }

        // 2. Resolve the Guest source Cart by its opaque identifier.
        CartId sourceId;

        try
        {
            sourceId = new CartId(command.GuestCartId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(CartErrorMapper.Validation(CartErrors.InvalidCartId));
        }

        var sourceState = await _cartStore.GetAsync(sourceId, cancellationToken).ConfigureAwait(false);

        if (sourceState is null)
        {
            return Result.Failure<CartState>(CartErrorMapper.ToApplicationError(CartErrors.CartNotFound));
        }

        // 3. Validate the source is Guest-owned from the persisted state — never trust the
        //    identifier alone. An authenticated Cart is never a valid Guest merge source.
        if (sourceState.UserId is not null)
        {
            return Result.Failure<CartState>(CartErrorMapper.ToApplicationError(CartErrors.CartAccessDenied));
        }

        // 4. Resolve (or atomically create) the current user's active target Cart.
        var targetResult = await AuthenticatedCartResolver.ResolveOrCreateAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
            cancellationToken).ConfigureAwait(false);

        if (targetResult.IsFailure)
        {
            return Result.Failure<CartState>(targetResult.Error);
        }

        var targetState = targetResult.Value!;

        // 5. Merge Guest items into the target through the existing Domain behavior. No item
        //    manipulation happens at the Application layer.
        var source = CartApplicationMapper.ToAggregate(sourceState);
        var target = CartApplicationMapper.ToAggregate(targetState);
        var merge = target.Merge(source, ToUtcOffset(_dateTimeProvider.UtcNow));

        if (merge.IsFailure)
        {
            return Result.Failure<CartState>(CartErrorMapper.ToApplicationError(merge.Error));
        }

        // 6. Atomically persist the merged authenticated Cart AND consume the Guest source in a
        //    single storage operation. Consumption happens only when the target write succeeds, so
        //    a failed merge never consumes the source and a retried merge can never duplicate the
        //    Guest quantities. The expected source version closes the concurrent double-apply race:
        //    a merge that read the source before a concurrent merge consumed it can no longer
        //    commit against a target that already contains the same Guest quantities.
        var save = await _cartStore.MergeAndConsumeAsync(
            CartApplicationMapper.ToState(target),
            expectedVersion: targetState.Version,
            expectedSourceVersion: sourceState.Version,
            sourceId,
            cancellationToken).ConfigureAwait(false);

        return save.Status switch
        {
            CartSaveStatus.Success => Result.Success(save.Cart ?? CartApplicationMapper.ToState(target)),
            CartSaveStatus.VersionConflict => Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description)),
            _ => Result.Failure<CartState>(CartErrorMapper.ToApplicationError(CartErrors.CartNotFound)),
        };
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
