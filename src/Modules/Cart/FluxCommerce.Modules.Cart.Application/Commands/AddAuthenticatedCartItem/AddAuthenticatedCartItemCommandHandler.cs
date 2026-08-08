using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;

public sealed class AddAuthenticatedCartItemCommandHandler
    : ICommandHandler<AddAuthenticatedCartItemCommand, Result<CartState>>
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;
    private readonly ICatalogVariantCartLookup _variantLookup;

    public AddAuthenticatedCartItemCommandHandler(
        ICartStore cartStore,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ICatalogVariantCartLookup variantLookup)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _variantLookup = variantLookup ?? throw new ArgumentNullException(nameof(variantLookup));
    }

    public async Task<Result<CartState>> HandleAsync(
        AddAuthenticatedCartItemCommand command,
        CancellationToken cancellationToken = default)
    {
        VariantId variantId;
        Money unitPrice;
        try
        {
            variantId = new VariantId(command.VariantId);
            unitPrice = new Money(command.UnitPriceAmount, command.Currency);
        }
        catch (ArgumentException)
        {
            return Result.Failure<CartState>(CartErrorMapper.Validation(CartErrors.InvalidCartItem));
        }

        var (eligibilityError, results) = await CartVariantEligibilityValidator.ValidateAsync(
            _variantLookup,
            [variantId.Value],
            cancellationToken).ConfigureAwait(false);

        if (eligibilityError is not null)
        {
            return Result.Failure<CartState>(eligibilityError);
        }

        // The CartItem snapshot comes from the Catalog contract, never from the caller. A malformed
        // result fails safely instead of persisting partial or inconsistent snapshot data.
        CartItemSnapshotData snapshot;
        try
        {
            snapshot = CartVariantEligibilityMapper.ToSnapshotData(
                results.Single(result => result.VariantId == variantId.Value));
        }
        catch (Exception)
        {
            return Result.Failure<CartState>(CartErrorMapper.ToApplicationError(CartErrors.CatalogOperationFailed));
        }

        return await AuthenticatedCartMutationExecutor.ExecuteAsync(
            _cartStore,
            _dateTimeProvider,
            _currentUser,
            (cart, utcNow) => cart.AddItem(
                snapshot.ProductId,
                variantId,
                snapshot.Sku,
                snapshot.ProductName,
                unitPrice,
                command.Quantity,
                utcNow),
            cancellationToken).ConfigureAwait(false);
    }
}
