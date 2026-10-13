namespace FluxCommerce.Api.Models;

/// <summary>
/// Succeeded-payment revenue for a single currency on a single day. Multi-currency is preserved
/// as an array, following the <c>totalsByCurrency</c> convention.
/// </summary>
public sealed record DailyRevenueResponse(
    string Currency,
    decimal Amount);

/// <summary>
/// One day of the dashboard series (Phase 23 F3): order demand plus succeeded-payment revenue.
/// Days with no activity carry <c>0</c> orders and empty revenue so charts keep a stable shape.
/// </summary>
public sealed record DailySeriesPointResponse(
    string Date,
    int Orders,
    IReadOnlyList<DailyRevenueResponse> Revenue);
