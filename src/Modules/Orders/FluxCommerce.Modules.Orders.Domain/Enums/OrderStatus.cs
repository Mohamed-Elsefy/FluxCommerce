namespace FluxCommerce.Modules.Orders.Domain.Enums;

/// <summary>
/// Phase 10.4 adds <see cref="Confirmed"/> — order confirmed after successful payment
/// (WaitingForPayment → Completed via SucceedPayment). Pending → Confirmed is the happy path;
/// Pending → Cancelled remains the inventory-failure and compensation path.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Cancelled = 1,

    /// <summary>
    /// Phase 10.4: payment succeeded, order confirmed. Terminal success.
    /// </summary>
    Confirmed = 2,
}
