using FluxCommerce.Api.Constants;
using FluxCommerce.Api.Extensions;
using FluxCommerce.Api.Models;
using FluxCommerce.Api.Services;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Admin;

/// <summary>
/// Adaptive administrative dashboard snapshot (Phase 22 E1): any authenticated user receives
/// <c>200 OK</c>, and every section is included only when the caller is entitled to it â€”
/// <c>orders.read</c> for orders, <c>payments.read</c> or <c>payments.stats</c> for payments,
/// <c>inventory.read</c> for inventory, <c>catalog.manage</c> for catalog, <c>users.read</c> for
/// users. Recent payments require <c>payments.read</c> specifically (stats-only viewers get the
/// stats but no list), mirroring the orders read/list split. The daily series merges both sides
/// and is included when either is entitled, with the unauthorized side zeroed. Composition lives
/// in <see cref="IDashboardComposer"/>; this controller only validates input and resolves
/// entitlements from the caller's permissions.
/// </summary>
[ApiController]
[Route("admin/dashboard")]
[Tags(EndpointTags.Dashboard)]
[Authorize]
public sealed class AdminDashboardController : ApiControllerBase
{
    private const int DefaultDailySeriesDays = 30;
    private const int MaxDailySeriesDays = 90;
    private const int DefaultLowStockThreshold = 5;

    private readonly ICurrentUser _currentUser;
    private readonly IDashboardComposer _composer;

    public AdminDashboardController(
        ICurrentUser currentUser,
        IDashboardComposer composer,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
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

        var entitlements = new DashboardEntitlements(
            Orders: HasPermission(PermissionCatalog.OrdersRead),
            PaymentsList: HasPermission(PermissionCatalog.PaymentsRead),
            PaymentsStats: HasPermission(PermissionCatalog.PaymentsStats),
            Inventory: HasPermission(PermissionCatalog.InventoryRead),
            Catalog: HasPermission(PermissionCatalog.CatalogManage),
            Users: HasPermission(PermissionCatalog.UsersRead));

        var result = await _composer.ComposeAsync(
            new DashboardSnapshotQuery(lowStockThreshold ?? DefaultLowStockThreshold, windowDays, entitlements),
            cancellationToken);

        return Success(result);
    }

    private bool HasPermission(string permission) =>
        IsSuperAdmin() ||
        _currentUser.Permissions.Contains(permission, StringComparer.Ordinal);

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal);
}
