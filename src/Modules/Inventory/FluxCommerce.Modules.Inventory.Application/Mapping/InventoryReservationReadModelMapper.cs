using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;

namespace FluxCommerce.Modules.Inventory.Application.Mapping;

/// <summary>
/// Maps Inventory aggregates to the module's application read models.
/// </summary>
public static class InventoryReservationReadModelMapper
{
    public static InventoryReservationReadModel ToReadModel(InventoryReservation reservation) =>
        new(
            reservation.Id.Value,
            reservation.OrderId.Value,
            reservation.Sku.Value,
            reservation.Quantity,
            reservation.Status,
            reservation.ExpiresAt,
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc,
            reservation.Version);
}
