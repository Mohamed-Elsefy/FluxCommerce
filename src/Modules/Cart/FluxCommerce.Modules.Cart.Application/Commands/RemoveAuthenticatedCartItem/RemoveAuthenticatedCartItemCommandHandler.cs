using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;

public sealed class RemoveAuthenticatedCartItemCommandHandler
    : ICommandHandler<RemoveAuthenticatedCartItemCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;

    public RemoveAuthenticatedCartItemCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<CartState>> HandleAsync(
        RemoveAuthenticatedCartItemCommand command,
        CancellationToken cancellationToken = default)
    {
        VariantId variantId;
        try
        {
            variantId = new VariantId(command.VariantId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(CartErrorMapper.Validation(CartErrors.InvalidCartItem));
        }

        return await AuthenticatedCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
            (cart, utcNow) => cart.RemoveItem(variantId, utcNow),
            cancellationToken).ConfigureAwait(false);
    }
}

