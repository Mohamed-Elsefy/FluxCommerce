using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;

public sealed class AddGuestCartItemCommandHandler : ICommandHandler<AddGuestCartItemCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddGuestCartItemCommandHandler(ICartStore cartStore, IDateTimeProvider dateTimeProvider)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<CartState>> HandleAsync(
        AddGuestCartItemCommand command,
        CancellationToken cancellationToken = default)
    {
        VariantId variantId;
        ProductId productId;
        Sku sku;
        Money unitPrice;
        try
        {
            variantId = new VariantId(command.VariantId);
            productId = new ProductId(command.ProductId);
            sku = new Sku(command.Sku);
            unitPrice = new Money(command.UnitPriceAmount, command.Currency);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(CartErrorMapper.Validation(CartErrors.InvalidCartItem));
        }

        if (string.IsNullOrWhiteSpace(command.ProductName) ||
            command.ProductName.Trim().Length > CartItem.ProductNameMaxLength)
        {
            return Result.Failure<CartState>(CartErrorMapper.Validation(CartErrors.InvalidCartItem));
        }

        return await GuestCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            command.CartId,
            (cart, utcNow) => cart.AddItem(
                productId,
                variantId,
                sku,
                command.ProductName.Trim(),
                unitPrice,
                command.Quantity,
                utcNow),
            cancellationToken).ConfigureAwait(false);
    }
}
