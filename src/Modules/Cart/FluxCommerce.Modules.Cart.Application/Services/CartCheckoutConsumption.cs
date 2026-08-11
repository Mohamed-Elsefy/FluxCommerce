using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Mappings;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Services;

/// <summary>
/// Post-commit Checkout Cart consumption. Runs as the best-effort tail of a successful Order
/// placement and clears the purchased Cart through the same optimistic-concurrency path as the item
/// commands. The consumer is a strict boundary against the Orders flow: it never throws, it only
/// clears the Cart owned by the supplied customer, and every outcome is reported as a
/// <see cref="CartCheckoutConsumptionStatus"/> so a failing Cart clear can never roll back or
/// duplicate the already-persisted Order.
/// </summary>
public sealed class CartCheckoutConsumption : ICartCheckoutConsumption
{
    private readonly ICartStore _cartStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CartCheckoutConsumption(ICartStore cartStore, IDateTimeProvider dateTimeProvider)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<CartCheckoutConsumptionResult> ConsumeAsync(
        Guid cartId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        CartState? state;
        try
        {
            state = await _cartStore.GetAsync(new CartId(cartId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.Failed);
        }

        if (state is null)
            return new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.CartNotFound);

        // The persisted ownership is the only authority: never clear a Guest Cart or another
        // customer's Cart, even though the consuming flow already validated the Checkout snapshot.
        if (state.UserId is null || state.UserId.Value != customerId)
            return new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.AccessDenied);

        if (state.Items.Count == 0)
            return new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.AlreadyEmpty);

        var cart = CartApplicationMapper.ToAggregate(state);

        cart.Clear(ToUtcOffset(_dateTimeProvider.UtcNow));

        CartSaveResult save;
        try
        {
            save = await _cartStore.SaveAsync(
                CartApplicationMapper.ToState(cart),
                expectedVersion: state.Version,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.Failed);
        }

        return save.Status switch
        {
            CartSaveStatus.Success => new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.Consumed),
            CartSaveStatus.VersionConflict => new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.ConcurrencyConflict),
            _ => new CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus.Failed),
        };
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}