namespace FluxCommerce.Modules.Payments.Domain.Enums;

/// <summary>
/// Phase 10.8 adds <see cref="Refunded"/> for post-payment compensation.
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,

    /// <summary>
    /// Phase 10.8: payment was refunded via Stripe after post-payment failure.
    /// </summary>
    Refunded = 5
}