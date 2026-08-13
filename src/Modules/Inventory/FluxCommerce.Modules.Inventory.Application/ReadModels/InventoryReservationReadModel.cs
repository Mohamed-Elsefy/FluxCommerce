using FluxCommerce.Modules.Inventory.Domain.Enums;

namespace FluxCommerce.Modules.Inventory.Application.ReadModels;

/// <summary>
/// Read-side projection of an <c>InventoryReservation</c> for the reservation lifecycle use cases.
/// </summary>
public sealed record InventoryReservationReadModel(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    InventoryReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
