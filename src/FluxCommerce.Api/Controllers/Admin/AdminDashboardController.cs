using FluxCommerce.Api.Extensions;
using FluxCommerce.Api.Models;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCatalogCounts;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;
using OrderDailyPointReadModel = FluxCommerce.Modules.Orders.Application.ReadModels.OrderDailyPointReadModel;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentDailyPoints;
using FluxCommerce.Modules.Payments.Domain.Enums;
using PaymentDailyPointReadModel = FluxCommerce.Modules.Payments.Application.ReadModels.PaymentDailyPointReadModel;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Admin;

/// <summary>
/// Adaptive administrative dashboard snapshot (Phase 22 E1): any authenticated user receives
/// <c>200 OK</c>, and every section is included only when the caller is entitled to it —
/// <c>orders.read</c> for orders, <c>payments.read</c> or <c>payments.stats</c> for payments,
/// <c>inventory.read</c> for inventory, <c>catalog.manage</c> for catalog, <c>users.read</c> for
/// users. Recent payments require <c>payments.read</c> specifically (stats-only viewers get the
/// stats but no list), mirroring the orders read/list split. The daily series merges both sides
/// and is included when either is entitled, with the unauthorized side zeroed. Each section is
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
    private const int RecentPaymentsLimit = 5;
    private const int RecentUsersLimit = 5;
    private const int DefaultDailySeriesDays = 30;
    private const int MaxDailySeriesDays = 90;
    private const int DefaultLowStockThreshold = 5;

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> _orderCountsHandler;
    private readonly IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _recentOrdersHandler;
    private readonly IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>> _orderPointsHandler;
    private readonly IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> _paymentStatsHandler;
    private readonly IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> _recentPaymentsHandler;
    private readonly IQueryHandler<GetPaymentDailyPointsQuery, Result<IReadOnlyList<PaymentDailyPointReadModel>>> _paymentPointsHandler;
    private readonly IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> _inventoryCountsHandler;
    private readonly IQueryHandler<GetCatalogCountsQuery, Result<CatalogCountsResponse>> _catalogCountsHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _userCountsHandler;
    private readonly IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> _recentUsersHandler;

    public AdminDashboardController(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> orderCountsHandler,
        IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> recentOrdersHandler,
        IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>> orderPointsHandler,
        IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> paymentStatsHandler,
        IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> recentPaymentsHandler,
        IQueryHandler<GetPaymentDailyPointsQuery, Result<IReadOnlyList<PaymentDailyPointReadModel>>> paymentPointsHandler,
        IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> inventoryCountsHandler,
        IQueryHandler<GetCatalogCountsQuery, Result<CatalogCountsResponse>> catalogCountsHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> userCountsHandler,
        IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> recentUsersHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _orderCountsHandler = orderCountsHandler;
        _recentOrdersHandler = recentOrdersHandler;
        _orderPointsHandler = orderPointsHandler;
        _paymentStatsHandler = paymentStatsHandler;
        _recentPaymentsHandler = recentPaymentsHandler;
        _paymentPointsHandler = paymentPointsHandler;
        _inventoryCountsHandler = inventoryCountsHandler;
        _catalogCountsHandler = catalogCountsHandler;
        _userCountsHandler = userCountsHandler;
        _recentUsersHandler = recentUsersHandler;
    }

    /// <summary>
    /// Returns the administrative dashboard snapshot with only the entitled sections.
    /// The low-stock threshold applies to the inventory section only and defaults to 5
    /// available units; a negative threshold is rejected regardless of entitlement.
    /// The daily series covers the trailing <c>days</c> calendar days (default 30, max 90)
    /// and is included when the caller sees orders or payments; the unauthorized side is
    /// zeroed so charts keep a stable shape.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] int? lowStockThreshold,
        [FromQuery] int? days,
        CancellationToken cancellationToken)
    {
        if (lowStockThreshold.HasValue && lowStockThreshold.Value < 0)
            return Success(Result<AdminDashboardResponse>.Failure(
                Error.Validation("Inventory.ValidationFailed", "The low-stock threshold must not be negative.")));

        var windowDays = days ?? DefaultDailySeriesDays;
        if (windowDays < 1 || windowDays > MaxDailySeriesDays)
            return Success(Result<AdminDashboardResponse>.Failure(
                Error.Validation("Dashboard.ValidationFailed", "The days window must be between 1 and 90.")));

        AdminOrderCountsResponse? orders = null;
        IReadOnlyList<OrderSummaryResponse> recentOrders = [];
        PaymentAdminStatsResponse? payments = null;
        IReadOnlyList<PaymentAdminResponse> recentPayments = [];
        InventoryCountsResponse? inventory = null;
        CatalogCountsResponse? catalog = null;
        UserAdminCountsResponse? users = null;
        IReadOnlyList<UserAdminResponse> recentUsers = [];
        IReadOnlyList<DailySeriesPointResponse> dailySeries = [];

        var canSeeOrders = HasPermission(PermissionCatalog.OrdersRead);
        var canSeePayments = HasPermission(PermissionCatalog.PaymentsRead) || HasPermission(PermissionCatalog.PaymentsStats);

        if (canSeeOrders || canSeePayments)
        {
            IReadOnlyList<DateTimeOffset> orderPoints = [];
            if (canSeeOrders)
            {
                var orderDaily = await _orderPointsHandler.HandleAsync(new GetOrderDailyPointsQuery(windowDays), cancellationToken);
                if (orderDaily.IsFailure)
                    return Success(Result<AdminDashboardResponse>.Failure(orderDaily.Error));

                orderPoints = orderDaily.Value!.Select(point => point.CreatedAtUtc).ToList();
            }

            IReadOnlyList<DailySeriesBuilder.PaymentRevenueInput> paymentPoints = [];
            if (canSeePayments)
            {
                var paymentDaily = await _paymentPointsHandler.HandleAsync(new GetPaymentDailyPointsQuery(windowDays), cancellationToken);
                if (paymentDaily.IsFailure)
                    return Success(Result<AdminDashboardResponse>.Failure(paymentDaily.Error));

                paymentPoints = paymentDaily.Value!
                    .Select(point => new DailySeriesBuilder.PaymentRevenueInput(
                        point.CreatedAtUtc,
                        point.Amount,
                        point.Currency,
                        point.Status == PaymentStatus.Succeeded))
                    .ToList();
            }

            dailySeries = DailySeriesBuilder.Build(
                orderPoints,
                paymentPoints,
                windowDays,
                new DateTimeOffset(_dateTimeProvider.UtcNow, TimeSpan.Zero));
        }

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

        if (HasPermission(PermissionCatalog.PaymentsRead))
        {
            var recent = await _recentPaymentsHandler.HandleAsync(
                new GetAdminPaymentsQuery(1, RecentPaymentsLimit, null, null, null, null, null),
                cancellationToken);
            if (recent.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(recent.Error));

            recentPayments = recent.Value!.Items.ToList();
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

        if (HasPermission(PermissionCatalog.UsersRead))
        {
            var userCounts = await _userCountsHandler.HandleAsync(new GetAdminUserCountsQuery(), cancellationToken);
            if (userCounts.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(userCounts.Error));

            users = userCounts.Value;

            var recent = await _recentUsersHandler.HandleAsync(
                new GetAdminUsersQuery(1, RecentUsersLimit, null, null, null),
                cancellationToken);
            if (recent.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(recent.Error));

            recentUsers = recent.Value!.Items.ToList();
        }

        if (HasPermission(PermissionCatalog.CatalogManage))
        {
            var catalogCounts = await _catalogCountsHandler.HandleAsync(new GetCatalogCountsQuery(), cancellationToken);
            if (catalogCounts.IsFailure)
                return Success(Result<AdminDashboardResponse>.Failure(catalogCounts.Error));

            catalog = catalogCounts.Value;
        }

        return Success(Result.Success(new AdminDashboardResponse(orders, payments, inventory, catalog, users, recentOrders, recentPayments, recentUsers, dailySeries)));
    }

    private bool HasPermission(string permission) =>
        IsSuperAdmin() ||
        _currentUser.Permissions.Contains(permission, StringComparer.Ordinal);

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal);
}
