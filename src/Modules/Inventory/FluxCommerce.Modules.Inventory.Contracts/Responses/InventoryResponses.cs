using FluxCommerce.Modules.Inventory.Contracts.Enums;

namespace FluxCommerce.Modules.Inventory.Contracts.Responses;

/// <summary>
/// Inventory item stock state returned by the Inventory management endpoints.
/// </summary>
public sealed record InventoryItemResponse(
    string Sku,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Reservation projection returned by the Inventory reservation lookup endpoint.
/// </summary>
public sealed record InventoryReservationResponse(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    InventoryReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
