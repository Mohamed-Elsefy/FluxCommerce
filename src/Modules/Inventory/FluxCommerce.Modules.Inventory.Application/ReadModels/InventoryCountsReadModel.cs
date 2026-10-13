using FluxCommerce.Modules.Inventory.Domain.Enums;

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
    int ExpiredReservations)
{
    /// <summary>
    /// Builds the overview from raw database parts. The store runs the totals and
    /// grouping queries; the reservation-status breakdown lives here in Application
    /// so the store stays a thin persistence adapter.
    /// </summary>
    public static InventoryCountsReadModel FromParts(
        int totalItems,
        int totalOnHand,
        int totalReserved,
        int lowStockItems,
        IEnumerable<(InventoryReservationStatus Status, int Count)> reservationGroups)
    {
        ArgumentNullException.ThrowIfNull(reservationGroups);

        var pending = 0;
        var committed = 0;
        var released = 0;
        var expired = 0;

        foreach (var group in reservationGroups)
        {
            switch (group.Status)
            {
                case InventoryReservationStatus.Pending:
                    pending = group.Count;
                    break;
                case InventoryReservationStatus.Committed:
                    committed = group.Count;
                    break;
                case InventoryReservationStatus.Released:
                    released = group.Count;
                    break;
                case InventoryReservationStatus.Expired:
                    expired = group.Count;
                    break;
            }
        }

        return new InventoryCountsReadModel(
            totalItems,
            totalOnHand,
            totalReserved,
            lowStockItems,
            pending,
            committed,
            released,
            expired);
    }
}
