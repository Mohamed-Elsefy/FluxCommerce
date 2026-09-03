using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
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

    /// <summary>
    /// Atomically persists the merged target Cart through the same optimistic-concurrency CAS used
    /// by <see cref="SaveAsync"/> AND consumes (deletes) the Guest source Cart in a single storage
    /// operation. The write succeeds only when the target version still equals
    /// <paramref name="expectedVersion"/> AND the Guest source Cart is still present with version
    /// equal to <paramref name="expectedSourceVersion"/>. The source-version check closes the
    /// concurrent double-apply race, so a merge attempt that read the source before a concurrent
    /// attempt consumed it can never commit against a target that already contains the same Guest
    /// quantities. The source is removed only when the target write succeeds, so a failed merge
    /// never consumes the source and a retried merge can never re-apply the Guest quantities. The
    /// target must be the current user's own Cart and the source must be Guest-owned.
    /// </summary>
    Task<CartSaveResult> MergeAndConsumeAsync(
        CartState target,
        long expectedVersion,
        long expectedSourceVersion,
        CartId sourceCartId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default);
}
