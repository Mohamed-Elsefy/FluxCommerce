namespace FluxCommerce.Modules.Payments.Contracts.Responses;

/// <summary>
/// Administrative Payment representation. Returned by <c>GET /admin/payments</c> and
/// <c>GET /admin/payments/{paymentId}</c>. Unlike the customer endpoint (anonymous
/// object), the admin surface uses an explicit contract.
/// </summary>
public sealed record PaymentAdminResponse(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderPaymentIntentId,
    string? Provider,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>
/// Succeeded-payment totals for a single currency.
/// </summary>
public sealed record PaymentCurrencyTotalResponse(
    string Currency,
    int SucceededCount,
    decimal SucceededTotalAmount);

/// <summary>
/// Aggregate Payment stats for the administrative overview.
/// Returned by <c>GET /admin/payments/stats</c>.
/// <c>RefundedTotalsByCurrency</c> reuses the same currency-total shape so net
/// revenue stays computable from one snapshot (Phase 23 F1).
/// </summary>
public sealed record PaymentAdminStatsResponse(
    int Total,
    int Pending,
    int Processing,
    int Succeeded,
    int Failed,
    int Cancelled,
    int Refunded,
    IReadOnlyList<PaymentCurrencyTotalResponse> TotalsByCurrency,
    IReadOnlyList<PaymentCurrencyTotalResponse> RefundedTotalsByCurrency);
