using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;

public sealed class ClearGuestCartCommandHandler : ICommandHandler<ClearGuestCartCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ClearGuestCartCommandHandler(ICartStore cartStore, IDateTimeProvider dateTimeProvider)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public Task<Result<CartState>> HandleAsync(
        ClearGuestCartCommand command,
        CancellationToken cancellationToken = default) =>
        GuestCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            command.CartId,
            (cart, utcNow) => cart.Clear(utcNow),
            cancellationToken);
}

