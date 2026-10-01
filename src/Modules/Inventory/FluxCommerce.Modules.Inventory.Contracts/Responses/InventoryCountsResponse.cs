namespace FluxCommerce.Modules.Inventory.Contracts.Responses;

/// <summary>
/// Aggregate Inventory counts for the administrative overview.
/// Returned by <c>GET /inventory/counts</c>.
/// </summary>
public sealed record InventoryCountsResponse(
    int TotalItems,
    int TotalOnHand,
    int TotalReserved,
    int LowStockItems,
    int PendingReservations,
    int CommittedReservations,
    int ReleasedReservations,
    int ExpiredReservations);
