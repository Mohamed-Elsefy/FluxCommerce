using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

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

    public Task<Result<CartState>> HandleAsync(
        CreateAuthenticatedCartCommand command,
        CancellationToken cancellationToken = default) =>
        AuthenticatedCartResolver.ResolveOrCreateAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
            cancellationToken);
}
