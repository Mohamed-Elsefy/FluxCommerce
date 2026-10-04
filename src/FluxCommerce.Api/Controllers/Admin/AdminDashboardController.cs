using FluxCommerce.Api.Extensions;
using FluxCommerce.Api.Models;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Admin;

/// <summary>
/// Adaptive administrative dashboard snapshot (Phase 22 E1): any authenticated user receives
/// <c>200 OK</c>, and every section is included only when the caller is entitled to it —
/// <c>orders.read</c> for orders, <c>payments.read</c> or <c>payments.stats</c> for payments,
/// <c>inventory.read</c> for inventory, the <c>SuperAdmin</c> role for users. Each section is
/// produced by its owning module's read path; the host only composes the results and never
/// touches another module's state.
/// </summary>
[ApiController]
[Route("admin/dashboard")]
[Tags(EndpointTags.Dashboard)]
[Authorize]
public sealed class AdminDashboardController : ApiControllerBase
{
    private const int RecentOrdersLimit = 5;
    private const int DefaultLowStockThreshold = 5;

    private readonly ICurrentUser _currentUser;
    private readonly IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> _orderCountsHandler;
    private readonly IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _recentOrdersHandler;
    private readonly IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> _paymentStatsHandler;
    private readonly IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> _inventoryCountsHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _userCountsHandler;

    public AdminDashboardController(
        ICurrentUser currentUser,
        IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> orderCountsHandler,
        IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> recentOrdersHandler,
        IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> paymentStatsHandler,
        IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> inventoryCountsHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> userCountsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _orderCountsHandler = orderCountsHandler;
        _recentOrdersHandler = recentOrdersHandler;
        _paymentStatsHandler = paymentStatsHandler;
        _inventoryCountsHandler = inventoryCountsHandler;
        _userCountsHandler = userCountsHandler;
    }

    /// <summary>
    /// Returns the administrative dashboard snapshot with only the entitled sections.
    /// The low-stock threshold applies to the inventory section only and defaults to 5
    /// available units; a negative threshold is rejected regardless of entitlement.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] int? lowStockThreshold,
        CancellationToken cancellationToken)
    {
        if (lowStockThreshold.HasValue && lowStockThreshold.Value < 0)
            return Success(Result<AdminDashboardResponse>.Failure(
                Error.Validation("Inventory.ValidationFailed", "The low-stock threshold must not be negative.")));

        AdminOrderCountsResponse? orders = null;
        IReadOnlyList<OrderSummaryResponse> recentOrders = [];
        PaymentAdminStatsResponse? payments = null;
        InventoryCountsResponse? inventory = null;
        UserAdminCountsResponse? users = null;

        if (HasPermission(PermissionCatalog.OrdersRead))
        {
            var orderCounts = await _orderCountsHandler.HandleAsync(new GetAdminOrderCountsQuery(), cancellationToken);
            if (orderCounts.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(orderCounts.Error));

            orders = orderCounts.Value;

            var recent = await _recentOrdersHandler.HandleAsync(
                new GetAdminOrdersQuery(1, RecentOrdersLimit, null, null, null, null, null),
                cancellationToken);
            if (recent.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(recent.Error));

            recentOrders = recent.Value!.Items.ToList();
        }

        if (HasPermission(PermissionCatalog.PaymentsRead) || HasPermission(PermissionCatalog.PaymentsStats))
        {
            var paymentStats = await _paymentStatsHandler.HandleAsync(new GetAdminPaymentStatsQuery(), cancellationToken);
            if (paymentStats.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(paymentStats.Error));

            payments = paymentStats.Value;
        }

        if (HasPermission(PermissionCatalog.InventoryRead))
        {
            var inventoryCounts = await _inventoryCountsHandler.HandleAsync(
                new GetInventoryCountsQuery(lowStockThreshold ?? DefaultLowStockThreshold),
                cancellationToken);
            if (inventoryCounts.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(inventoryCounts.Error));

            var counts = inventoryCounts.Value!;
            inventory = new InventoryCountsResponse(
                counts.TotalItems,
                counts.TotalOnHand,
                counts.TotalReserved,
                counts.LowStockItems,
                counts.PendingReservations,
                counts.CommittedReservations,
                counts.ReleasedReservations,
                counts.ExpiredReservations);
        }

        if (IsSuperAdmin())
        {
            var userCounts = await _userCountsHandler.HandleAsync(new GetAdminUserCountsQuery(), cancellationToken);
            if (userCounts.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(userCounts.Error));

            users = userCounts.Value;
        }

        return Success(Result.Success(new AdminDashboardResponse(orders, payments, inventory, users, recentOrders)));
    }

    private bool HasPermission(string permission) =>
        IsSuperAdmin() ||
        _currentUser.Permissions.Contains(permission, StringComparer.Ordinal);

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal);
}
