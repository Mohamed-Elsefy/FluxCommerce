using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;

public sealed class ClearAuthenticatedCartCommandHandler
    : ICommandHandler<ClearAuthenticatedCartCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;

    public ClearAuthenticatedCartCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public Task<Result<CartState>> HandleAsync(
        ClearAuthenticatedCartCommand command,
        CancellationToken cancellationToken = default) =>
        AuthenticatedCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
            (cart, utcNow) => cart.Clear(utcNow),
            cancellationToken);
}
