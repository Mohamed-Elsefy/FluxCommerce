using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;

/// <summary>
/// Releases a Pending reservation, returning its reserved quantity to available stock. A terminal
/// reservation cannot be released.
/// </summary>
public sealed record ReleaseReservationCommand(Guid ReservationId)
    : ICommand<Result<InventoryReservationReadModel>>;
