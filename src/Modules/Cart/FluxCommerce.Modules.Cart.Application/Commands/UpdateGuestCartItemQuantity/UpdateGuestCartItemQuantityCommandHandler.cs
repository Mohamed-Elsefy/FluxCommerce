using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Services;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;

public sealed class UpdateGuestCartItemQuantityCommandHandler
    : ICommandHandler<UpdateGuestCartItemQuantityCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateGuestCartItemQuantityCommandHandler(ICartStore cartStore, IDateTimeProvider dateTimeProvider)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<CartState>> HandleAsync(
        UpdateGuestCartItemQuantityCommand command,
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

        return await GuestCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            command.CartId,
            (cart, utcNow) => cart.UpdateQuantity(variantId, command.Quantity, utcNow),
            cancellationToken).ConfigureAwait(false);
    }
}

