using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Application.Ownership;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;

public sealed class CreateAuthenticatedCartCommandHandler
    : ICommandHandler<CreateAuthenticatedCartCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateAuthenticatedCartCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<CartState>> HandleAsync(
        CreateAuthenticatedCartCommand command,
        CancellationToken cancellationToken = default)
    {
        var owner = AuthenticatedCartOwnership.ResolveAuthenticatedOwner(_currentUser);

        if (owner.IsFailure)
        {
            return Result.Failure<CartState>(owner.Error);
        }

        var userId = owner.Value.UserId!.Value;

        // One active Cart per user: a repeated create returns the existing active Cart unchanged.
        var existing = await _cartStore.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            var ownership = AuthenticatedCartOwnership.ValidateOwnership(existing, _currentUser);

            if (ownership.IsFailure)
            {
                return Result.Failure<CartState>(ownership.Error);
            }

            return Result.Success(existing);
        }

        var utcNow = ToUtcOffset(_dateTimeProvider.UtcNow);

        var cart = CartAggregate.CreateAuthenticated(CartId.Create(), userId, utcNow);

        var state = CartApplicationMapper.ToState(cart);

        // Atomic create: cart key + user index key are written together only when the user has no
        // active Cart yet, so a concurrent create cannot produce a second active Cart.
        var save = await _cartStore.CreateForUserAsync(state, cancellationToken).ConfigureAwait(false);

        return save.Status switch
        {
            CartSaveStatus.Success => Result.Success(save.Cart ?? state),
            CartSaveStatus.DuplicateActiveCart => await ResolveConcurrentWinnerAsync(userId, cancellationToken).ConfigureAwait(false),
            _ => Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description)),
        };
    }

    private async Task<Result<CartState>> ResolveConcurrentWinnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var winner = await _cartStore.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return winner is not null
            ? Result.Success(winner)
            : Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description));
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
