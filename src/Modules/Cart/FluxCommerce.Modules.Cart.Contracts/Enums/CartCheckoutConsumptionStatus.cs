namespace FluxCommerce.Modules.Cart.Contracts.Enums;

/// <summary>
/// Outcome of a best-effort Cart consumption. Retryable conditions (concurrency) and non-retryable
/// conditions are kept distinct so the Orders Application could retry without duplicating work.
/// </summary>
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
