using FluxCommerce.Modules.Payments.Domain.Enums;

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
    IReadOnlyList<PaymentCurrencyTotalReadModel> RefundedTotalsByCurrency)
{
    /// <summary>
    /// Builds the overview from raw database parts. The store runs the status grouping
    /// and currency-total queries; the status breakdown lives here in Application so
    /// the store stays a thin persistence adapter.
    /// </summary>
    public static PaymentAdminStatsReadModel FromParts(
        IEnumerable<(PaymentStatus Status, int Count)> statusGroups,
        IReadOnlyList<PaymentCurrencyTotalReadModel> totalsByCurrency,
        IReadOnlyList<PaymentCurrencyTotalReadModel> refundedTotalsByCurrency)
    {
        ArgumentNullException.ThrowIfNull(statusGroups);
        ArgumentNullException.ThrowIfNull(totalsByCurrency);
        ArgumentNullException.ThrowIfNull(refundedTotalsByCurrency);

        var pending = 0;
        var processing = 0;
        var succeeded = 0;
        var failed = 0;
        var cancelled = 0;
        var refunded = 0;

        foreach (var group in statusGroups)
        {
            switch (group.Status)
            {
                case PaymentStatus.Pending:
                    pending = group.Count;
                    break;
                case PaymentStatus.Processing:
                    processing = group.Count;
                    break;
                case PaymentStatus.Succeeded:
                    succeeded = group.Count;
                    break;
                case PaymentStatus.Failed:
                    failed = group.Count;
                    break;
                case PaymentStatus.Cancelled:
                    cancelled = group.Count;
                    break;
                case PaymentStatus.Refunded:
                    refunded = group.Count;
                    break;
            }
        }

        return new PaymentAdminStatsReadModel(
            pending + processing + succeeded + failed + cancelled + refunded,
            pending,
            processing,
            succeeded,
            failed,
            cancelled,
            refunded,
            totalsByCurrency,
            refundedTotalsByCurrency);
    }
}
