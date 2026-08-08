using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;

public sealed class AddAuthenticatedCartItemCommandHandler
    : ICommandHandler<AddAuthenticatedCartItemCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;

    public AddAuthenticatedCartItemCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<CartState>> HandleAsync(
        AddAuthenticatedCartItemCommand command,
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

        return await AuthenticatedCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
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
