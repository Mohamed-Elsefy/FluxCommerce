namespace FluxCommerce.Modules.Identity.Domain.Aggregates;

/// <summary>
/// Single source of truth for the action-level permission catalog (ADR-016 D2).
/// Permissions travel as <see cref="ClaimType"/> claims on the access token and are stored
/// explicitly per user; the `Admin` role grants the <see cref="AdminBundle"/> at assignment time
/// (Phase D), while user/permission management itself stays `SuperAdmin`-only and is therefore
/// not part of any bundle.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>JWT claim type carrying one granted permission per claim.</summary>
    public const string ClaimType = "permission";

    public const string OrdersRead = "orders.read";
    public const string OrdersCancel = "orders.cancel";
    public const string OrdersRefund = "orders.refund";
    public const string PaymentsRead = "payments.read";
    public const string PaymentsStats = "payments.stats";
    public const string InventoryRead = "inventory.read";
    public const string InventoryManage = "inventory.manage";
    public const string CatalogManage = "catalog.manage";
    public const string DashboardRead = "dashboard.read";

    /// <summary>Every permission the system knows, for validation and catalog listing.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        OrdersRead,
        OrdersCancel,
        OrdersRefund,
        PaymentsRead,
        PaymentsStats,
        InventoryRead,
        InventoryManage,
        CatalogManage,
        DashboardRead,
    };

    /// <summary>Permissions granted automatically when the `Admin` role is assigned.</summary>
    public static readonly IReadOnlyCollection<string> AdminBundle = All;

    public static bool IsDefined(string? permission) =>
        !string.IsNullOrWhiteSpace(permission) &&
        All.Contains(permission.Trim(), StringComparer.Ordinal);
}
