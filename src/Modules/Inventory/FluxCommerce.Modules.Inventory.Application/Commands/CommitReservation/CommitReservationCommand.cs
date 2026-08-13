using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.CommitReservation;

/// <summary>
/// Commits a Pending reservation, consuming its reserved quantity as on-hand stock (the committed
/// quantity is removed from both on-hand and reserved). A terminal reservation cannot be committed.
/// </summary>
public sealed record CommitReservationCommand(Guid ReservationId)
    : ICommand<Result<InventoryReservationReadModel>>;
