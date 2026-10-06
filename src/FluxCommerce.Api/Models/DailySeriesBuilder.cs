using System.Globalization;

namespace FluxCommerce.Api.Models;

/// <summary>
/// In-memory day bucketing for the dashboard series (Phase 23 F3). The database only serves
/// lightweight filtered selects; grouping by calendar day runs here deliberately, because
/// date-part grouping translates differently on SQLite (<c>strftime</c>) and SQL Server
/// (<c>CAST/CONVERT</c>). The window covers the trailing calendar days ending today (UTC),
/// oldest first, zero-filled.
/// </summary>
public static class DailySeriesBuilder
{
    /// <summary>
    /// Payment input for revenue bucketing. <c>IsSucceeded</c> is resolved by the caller from
    /// the payment status so this builder stays free of module domain types.
    /// </summary>
    public sealed record PaymentRevenueInput(
        DateTimeOffset CreatedAtUtc,
        decimal Amount,
        string Currency,
        bool IsSucceeded);

    public static IReadOnlyList<DailySeriesPointResponse> Build(
        IReadOnlyList<DateTimeOffset> orderCreatedAtUtc,
        IReadOnlyList<PaymentRevenueInput> payments,
        int days,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(orderCreatedAtUtc);
        ArgumentNullException.ThrowIfNull(payments);

        if (days < 1)
            return [];

        var today = utcNow.UtcDateTime.Date;

        var ordersByDay = orderCreatedAtUtc
            .GroupBy(created => created.UtcDateTime.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        var revenueByDay = payments
            .Where(payment => payment.IsSucceeded)
            .GroupBy(payment => payment.CreatedAtUtc.UtcDateTime.Date)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DailyRevenueResponse>)group
                    .GroupBy(payment => payment.Currency, StringComparer.Ordinal)
                    .OrderBy(currency => currency.Key, StringComparer.Ordinal)
                    .Select(currency => new DailyRevenueResponse(
                        currency.Key,
                        currency.Sum(payment => payment.Amount)))
                    .ToList());

        var points = new List<DailySeriesPointResponse>(days);
        for (var offset = days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            ordersByDay.TryGetValue(day, out var orders);
            revenueByDay.TryGetValue(day, out var revenue);

            points.Add(new DailySeriesPointResponse(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                orders,
                revenue ?? []));
        }

        return points;
    }
}
