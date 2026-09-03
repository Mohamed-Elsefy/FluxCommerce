using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Services;

/// <summary>
/// Projects a Cart state into the Checkout-specific snapshot consumed by the Orders module. The
/// projection is a single Cart read from <see cref="ICartStore"/>; the Cart aggregate and store
/// remain internal to the Cart module.
/// </summary>
public sealed class CartCheckoutSnapshotProvider : ICartCheckoutSnapshotProvider
{
    private readonly ICartStore _cartStore;

    public CartCheckoutSnapshotProvider(ICartStore cartStore)
    {
        _cartStore = cartStore ?? throw new ArgumentNullException(nameof(cartStore));
    }

    public async Task<CartCheckoutSnapshot?> GetCheckoutSnapshotAsync(
        Guid cartId,
        CancellationToken cancellationToken = default)
    {
        var cart = await _cartStore.GetAsync(new CartId(cartId), cancellationToken).ConfigureAwait(false);

        if (cart is null)
            return null;

        var items = cart.Items
            .Select(item => new CartCheckoutItemSnapshot(
                item.ProductId.Value,
                item.VariantId.Value,
                item.Sku.Value,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency))
            .ToList();

        return new CartCheckoutSnapshot(cart.CartId.Value, cart.UserId, items);
    }
}