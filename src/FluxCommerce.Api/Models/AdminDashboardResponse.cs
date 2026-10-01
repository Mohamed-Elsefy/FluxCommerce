using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Api.Models;

/// <summary>
/// Administrative dashboard snapshot combining the per-module overviews with the newest
/// Orders. Every section is produced by its owning module's read path; the host only
/// composes the results and never touches another module's state.
/// </summary>
/// <param name="Orders">Order counts grouped by status.</param>
/// <param name="Payments">Payment counts grouped by status plus succeeded totals per currency.</param>
/// <param name="Inventory">Item totals and reservation counts grouped by status.</param>
/// <param name="Users">User counts grouped by status and role.</param>
/// <param name="RecentOrders">The newest Orders across customers (up to the requested limit).</param>
public sealed record AdminDashboardResponse(
    AdminOrderCountsResponse Orders,
    PaymentAdminStatsResponse Payments,
    InventoryCountsResponse Inventory,
    UserAdminCountsResponse Users,
    IReadOnlyList<OrderSummaryResponse> RecentOrders);
