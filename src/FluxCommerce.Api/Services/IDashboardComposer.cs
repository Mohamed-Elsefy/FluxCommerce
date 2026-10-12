using FluxCommerce.Api.Models;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Api.Services;

/// <summary>
/// Input for one dashboard snapshot composition. Thresholds are already validated
/// by the controller; the composer assumes non-negative values and a positive window.
/// </summary>
public sealed record DashboardSnapshotQuery(
    int LowStockThreshold,
    int WindowDays,
    DashboardEntitlements Entitlements);

/// <summary>
/// Composes the administrative dashboard snapshot from the owning modules' read
/// paths. Sections outside the caller's entitlements stay <c>null</c>/<c>empty</c>;
/// the first failing entitled section surfaces its error.
/// </summary>
public interface IDashboardComposer
{
    Task<Result<AdminDashboardResponse>> ComposeAsync(
        DashboardSnapshotQuery query,
        CancellationToken cancellationToken = default);
}
