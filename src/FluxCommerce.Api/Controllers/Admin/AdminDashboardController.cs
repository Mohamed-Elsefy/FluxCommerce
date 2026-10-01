using FluxCommerce.Api.Extensions;
using FluxCommerce.Api.Models;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Contracts.Responses;
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
/// Administrative dashboard snapshot: order, payment, inventory and user overviews plus
/// the newest Orders, in a single read-only call. All endpoints require the Admin role.
/// Each section is produced by its owning module's read path; the host only composes the
/// results and never touches another module's state.
/// </summary>
[ApiController]
[Route("admin/dashboard")]
[Tags(EndpointTags.Dashboard)]
[Authorize(Policy = AuthenticationConfiguration.DashboardReadPolicy)]
public sealed class AdminDashboardController : ApiControllerBase
{
    private const int RecentOrdersLimit = 5;

    private readonly IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> _orderCountsHandler;
    private readonly IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _recentOrdersHandler;
    private readonly IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> _paymentStatsHandler;
    private readonly IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> _inventoryCountsHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _userCountsHandler;

    public AdminDashboardController(
        IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> orderCountsHandler,
        IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> recentOrdersHandler,
        IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> paymentStatsHandler,
        IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> inventoryCountsHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> userCountsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _orderCountsHandler = orderCountsHandler;
        _recentOrdersHandler = recentOrdersHandler;
        _paymentStatsHandler = paymentStatsHandler;
        _inventoryCountsHandler = inventoryCountsHandler;
        _userCountsHandler = userCountsHandler;
    }

    /// <summary>
    /// Returns the administrative dashboard snapshot. The low-stock threshold applies to
    /// the inventory section only and defaults to 5 available units.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] int? lowStockThreshold,
        CancellationToken cancellationToken)
    {
        var orders = await _orderCountsHandler.HandleAsync(new GetAdminOrderCountsQuery(), cancellationToken);
        if (orders.IsFailure)
            return Success(Result<AdminDashboardResponse>.Failure(orders.Error));

        var recentOrders = await _recentOrdersHandler.HandleAsync(
            new GetAdminOrdersQuery(1, RecentOrdersLimit, null, null, null, null, null),
            cancellationToken);
        if (recentOrders.IsFailure)
            return Success(Result<AdminDashboardResponse>.Failure(recentOrders.Error));

        var payments = await _paymentStatsHandler.HandleAsync(new GetAdminPaymentStatsQuery(), cancellationToken);
        if (payments.IsFailure)
            return Success(Result<AdminDashboardResponse>.Failure(payments.Error));

        var inventory = await _inventoryCountsHandler.HandleAsync(
            new GetInventoryCountsQuery(lowStockThreshold),
            cancellationToken);
        if (inventory.IsFailure)
            return Success(Result<AdminDashboardResponse>.Failure(inventory.Error));

        var users = await _userCountsHandler.HandleAsync(new GetAdminUserCountsQuery(), cancellationToken);
        if (users.IsFailure)
            return Success(Result<AdminDashboardResponse>.Failure(users.Error));

        var counts = inventory.Value!;
        return Success(Result.Success(new AdminDashboardResponse(
            orders.Value!,
            payments.Value!,
            new InventoryCountsResponse(
                counts.TotalItems,
                counts.TotalOnHand,
                counts.TotalReserved,
                counts.LowStockItems,
                counts.PendingReservations,
                counts.CommittedReservations,
                counts.ReleasedReservations,
                counts.ExpiredReservations),
            users.Value!,
            recentOrders.Value!.Items.ToList())));
    }
}
