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

/// <summary>
/// Outcome of a best-effort Cart consumption. Retryable conditions (concurrency) and non-retryable
/// conditions are kept distinct so the Orders Application could retry without duplicating work.
/// </summary>
public sealed record CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus Status);

public enum CartCheckoutConsumptionStatus
{
    /// <summary>The Cart was loaded, ownership verified and its items cleared.</summary>
    Consumed = 0,

    /// <summary>The Cart exists and is owned by the customer but already contains no items.</summary>
    AlreadyEmpty = 1,

    /// <summary>No Cart exists for the supplied cart id.</summary>
    CartNotFound = 2,

    /// <summary>The Cart exists but belongs to a different customer (or is a Guest Cart).</summary>
    AccessDenied = 3,

    /// <summary>The Cart changed concurrently; the ownership read and the clear did not commit.</summary>
    ConcurrencyConflict = 4,

    /// <summary>Any other transient or permanent consumer failure. The Orders flow still succeeds.</summary>
    Failed = 5,
}