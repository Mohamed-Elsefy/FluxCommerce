using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Interfaces;

public interface ICartStore
{
    Task<CartState?> GetAsync(CartId cartId, CancellationToken cancellationToken = default);

    Task<CartState?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates an authenticated Cart together with its user index key. The write
    /// succeeds only when the user has no active Cart yet, which enforces the one-active-Cart
    /// rule even under concurrent creation.
    /// </summary>
    Task<CartSaveResult> CreateForUserAsync(CartState cart, CancellationToken cancellationToken = default);

    Task<CartSaveResult> SaveAsync(CartState cart, long expectedVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default);
}
