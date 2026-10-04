using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Api.Models;

/// <summary>
/// Administrative dashboard snapshot with per-section visibility (Phase 22 E1): every section is
/// included only when the caller is entitled to it, otherwise <c>null</c> (objects) or empty
/// (recent orders). Any authenticated user receives <c>200</c> — never a bare forbidden screen.
/// </summary>
/// <param name="Orders">Order counts, or <c>null</c> without <c>orders.read</c>.</param>
/// <param name="Payments">Payment stats, or <c>null</c> without <c>payments.read</c>/<c>stats</c>.</param>
/// <param name="Inventory">Inventory counts, or <c>null</c> without <c>inventory.read</c>.</param>
/// <param name="Users">User counts, or <c>null</c> for non-SuperAdmin callers.</param>
/// <param name="RecentOrders">The newest Orders (up to the limit), or empty without <c>orders.read</c>.</param>
public sealed record AdminDashboardResponse(
    AdminOrderCountsResponse? Orders,
    PaymentAdminStatsResponse? Payments,
    InventoryCountsResponse? Inventory,
    UserAdminCountsResponse? Users,
    IReadOnlyList<OrderSummaryResponse> RecentOrders);
