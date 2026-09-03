using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using CartAggregate = FluxCommerce.Modules.Cart.Domain.Aggregates.Cart;

namespace FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;

public sealed class CreateGuestCartCommandHandler : ICommandHandler<CreateGuestCartCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateGuestCartCommandHandler(ICartStore cartStore, IDateTimeProvider dateTimeProvider)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<CartState>> HandleAsync(
        CreateGuestCartCommand command,
        CancellationToken cancellationToken = default)
    {
        var utcNow = ToUtcOffset(_dateTimeProvider.UtcNow);

        var cart = CartAggregate.CreateGuest(CartId.Create(), utcNow);

        var state = CartApplicationMapper.ToState(cart);

        var save = await _cartStore.SaveAsync(state, expectedVersion: 0, cancellationToken).ConfigureAwait(false);

        return save.Status switch
        {
            CartSaveStatus.Success => Result.Success(save.Cart ?? state),
            _ => Result.Failure<CartState>(
                Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description)),
        };
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

