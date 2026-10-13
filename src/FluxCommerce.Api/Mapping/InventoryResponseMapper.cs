using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Responses;

namespace FluxCommerce.Api.Mapping;

/// <summary>
/// Host-side mapping for Inventory read models. Shared by the inventory endpoints
/// and the dashboard composer so the 8-field counts projection cannot drift.
/// </summary>
public static class InventoryResponseMapper
{
    public static InventoryCountsResponse ToCountsResponse(InventoryCountsReadModel counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return new InventoryCountsResponse(
            counts.TotalItems,
            counts.TotalOnHand,
            counts.TotalReserved,
            counts.LowStockItems,
            counts.PendingReservations,
            counts.CommittedReservations,
            counts.ReleasedReservations,
            counts.ExpiredReservations);
    }
}
