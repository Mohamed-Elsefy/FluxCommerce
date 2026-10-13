using FluxCommerce.Modules.Cart.Contracts.ReadModels;

namespace FluxCommerce.Modules.Cart.Contracts.Abstractions;

/// <summary>
/// Cross-module contract that lets the Orders Application clear a customer's Cart as the final,
/// best-effort step of a committed Checkout. Implemented by the Cart module; the consumer must
/// never throw — every outage or ownership/concurrency condition is reported as a status so the
/// Checkout flow can keep serving the already-persisted Order.
/// </summary>
public interface ICartCheckoutConsumption
{
    Task<CartCheckoutConsumptionResult> ConsumeAsync(
        Guid cartId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
