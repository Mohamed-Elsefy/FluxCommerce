namespace FluxCommerce.Api.Services;

/// <summary>
/// Per-section dashboard visibility, resolved by the controller from the caller's
/// permissions (SuperAdmin passes everything). The composer only reads the flags
/// and never touches identity itself.
/// </summary>
public sealed record DashboardEntitlements(
    bool Orders,
    bool PaymentsList,
    bool PaymentsStats,
    bool Inventory,
    bool Catalog,
    bool Users)
{
    /// <summary>
    /// The payments stats section is visible with either payments permission;
    /// the recent-payments list requires <c>payments.read</c> specifically.
    /// </summary>
    public bool Payments => PaymentsList || PaymentsStats;
}
