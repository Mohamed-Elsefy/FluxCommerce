using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence driven port for the customer-facing Orders queries and lifecycle commands. List
/// queries are served from a lightweight projection; single-order reads and cancellations load the
/// tracked aggregate (so a cancellation can mutate and flush the Order). Ownership is always enforced
/// by filtering on the current customer, so an Order can never be observed or cancelled by another
/// user. <see cref="GetByIdAsync"/> is the integration-event projection read: it loads the tracked
/// aggregate by identity alone (no customer scoping) because the Order Summary projection consumer
/// (Phase 7.9) reacts to events emitted by the same module.
/// </summary>
public interface IOrderStore
{
    Task<PagedResult<OrderSummaryReadModel>> GetMyOrdersPageAsync(
        CustomerId customerId,
        int pageNumber,
        int pageSize,
        OrderStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<Order?> GetOwnedOrderAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

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
