namespace FluxCommerce.Modules.Payments.Application.ReadModels;

/// <summary>
/// Succeeded-payment totals for a single currency.
/// </summary>
public sealed record PaymentCurrencyTotalReadModel(
    string Currency,
    int SucceededCount,
    decimal SucceededTotalAmount);

/// <summary>
/// Aggregate Payment stats for the administrative overview.
/// </summary>
public sealed record PaymentAdminStatsReadModel(
    int Total,
    int Pending,
    int Processing,
    int Succeeded,
    int Failed,
    int Cancelled,
    int Refunded,
    IReadOnlyList<PaymentCurrencyTotalReadModel> TotalsByCurrency,
    IReadOnlyList<PaymentCurrencyTotalReadModel> RefundedTotalsByCurrency);
