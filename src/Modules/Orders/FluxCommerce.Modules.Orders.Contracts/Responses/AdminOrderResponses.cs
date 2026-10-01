namespace FluxCommerce.Modules.Orders.Contracts.Responses;

/// <summary>
/// Aggregate Order counts for the administrative overview.
/// Returned by <c>GET /admin/orders/counts</c>.
/// </summary>
public sealed record AdminOrderCountsResponse(
    int Total,
    int Pending,
    int Confirmed,
    int Cancelled);
