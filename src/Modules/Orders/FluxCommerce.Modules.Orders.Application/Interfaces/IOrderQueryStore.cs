using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Query-side Orders persistence port: lightweight projections for customer lists,
/// administrative overviews, counts, and the dashboard daily series. Read-only;
/// implementations must not track aggregates through these members.
/// </summary>
public interface IOrderQueryStore
{
    Task<PagedResult<OrderSummaryReadModel>> GetMyOrdersPageAsync(
        CustomerId customerId,
        int pageNumber,
        int pageSize,
        OrderStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Administrative Order list: all customers with optional filters. No ownership scoping;
    /// the caller (API) must enforce <c>AdminPolicy</c>.
    /// </summary>
    Task<PagedResult<OrderSummaryReadModel>> GetAdminOrdersPageAsync(
        OrderStatus? status,
        Guid? customerId,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregate Order counts grouped by status for the administrative overview.
    /// </summary>
    Task<AdminOrderCountsReadModel> GetAdminOrderCountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Daily-series projection: creation timestamps of Orders created at or after
    /// <paramref name="fromUtc"/>. The date filter runs in memory over a lightweight timestamp
    /// stream (SQLite cannot translate <c>DateTimeOffset</c> comparisons); day bucketing also
    /// runs in memory at the consumer.
    /// </summary>
    Task<IReadOnlyList<OrderDailyPointReadModel>> GetCreatedPointsAsync(
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default);
}
