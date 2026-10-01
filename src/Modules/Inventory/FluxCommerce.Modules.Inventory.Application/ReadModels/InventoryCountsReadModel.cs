namespace FluxCommerce.Modules.Inventory.Application.ReadModels;

/// <summary>
/// Aggregate Inventory counts for the administrative overview: item totals plus
/// reservation counts grouped by status. <c>LowStockItems</c> counts items whose
/// available quantity is at or below the requested threshold.
/// </summary>
public sealed record InventoryCountsReadModel(
    int TotalItems,
    int TotalOnHand,
    int TotalReserved,
    int LowStockItems,
    int PendingReservations,
    int CommittedReservations,
    int ReleasedReservations,
    int ExpiredReservations);
