using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ExpireReservation;

/// <summary>
/// Expires a Pending reservation, safely releasing its reserved quantity back to available stock
/// according to the Domain expiration semantics. A terminal reservation cannot be expired again.
/// </summary>
public sealed record ExpireReservationCommand(Guid ReservationId)
    : ICommand<Result<InventoryReservationReadModel>>;
