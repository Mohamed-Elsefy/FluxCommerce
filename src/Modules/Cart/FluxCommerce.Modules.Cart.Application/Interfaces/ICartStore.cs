using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Application.Interfaces;

public interface ICartStore
{
    Task<CartState?> GetAsync(CartId cartId, CancellationToken cancellationToken = default);

    Task<CartState?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CartSaveResult> SaveAsync(CartState cart, long expectedVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default);
}
