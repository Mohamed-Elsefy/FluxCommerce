namespace FluxCommerce.Modules.Inventory.Contracts.Integration;

/// <summary>
/// Integration boundary for the future Order/Saga workflow: requests a reservation of
/// <see cref="Quantity"/> units of <see cref="Sku"/> on behalf of <see cref="OrderId"/> until
/// <see cref="ExpiresAt"/>. Consumers invoke the Inventory application use case with this request;
/// they never touch Inventory persistence.
/// </summary>
public sealed record ReserveInventoryRequest(
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Integration boundary for the future Order/Saga workflow: releases the reservation identified by
/// <see cref="ReservationId"/>, returning its reserved quantity to available stock.
/// </summary>
public sealed record ReleaseInventoryRequest(Guid ReservationId);

/// <summary>
/// Integration boundary for the future Order/Saga workflow: commits the reservation identified by
/// <see cref="ReservationId"/>, consuming its reserved quantity.
/// </summary>
public sealed record CommitInventoryRequest(Guid ReservationId);

/// <summary>
/// Result of an Inventory reservation lifecycle operation, surfaced to inter-module consumers. Carries
/// no Inventory internal state beyond the reservation projection.
/// </summary>
public sealed record InventoryReservationResult(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    Enums.InventoryReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
