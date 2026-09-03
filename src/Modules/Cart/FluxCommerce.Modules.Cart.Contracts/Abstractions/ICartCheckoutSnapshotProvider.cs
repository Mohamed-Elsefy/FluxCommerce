using FluxCommerce.Modules.Cart.Contracts.ReadModels;

namespace FluxCommerce.Modules.Cart.Contracts.Abstractions;

/// <summary>
/// Cross-module contract that lets the Orders Application consume the minimum required Cart data at
/// Checkout without touching Cart Redis, the Cart aggregate or any Cart infrastructure. Implemented
/// by the Cart module.
/// </summary>
public interface ICartCheckoutSnapshotProvider
{
    Task<CartCheckoutSnapshot?> GetCheckoutSnapshotAsync(
        Guid cartId,
        CancellationToken cancellationToken = default);
}