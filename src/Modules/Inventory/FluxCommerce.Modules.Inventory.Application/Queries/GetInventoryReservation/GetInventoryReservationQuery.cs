using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryReservation;

/// <summary>
/// Reads the current state of a single reservation by its identifier.
/// </summary>
public sealed record GetInventoryReservationQuery(Guid ReservationId)
    : IQuery<Result<InventoryReservationReadModel>>;
