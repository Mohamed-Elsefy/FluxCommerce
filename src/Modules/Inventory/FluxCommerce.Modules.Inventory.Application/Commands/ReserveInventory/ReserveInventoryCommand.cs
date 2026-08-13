using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;

/// <summary>
/// Reserves <see cref="Quantity"/> units of <see cref="Sku"/> on behalf of <see cref="OrderId"/> until
/// <see cref="ExpiresAt"/>. The reservation succeeds only while the requested quantity is available;
/// an insufficient-stock attempt fails without changing inventory state.
/// </summary>
public sealed record ReserveInventoryCommand(
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset ExpiresAt)
    : ICommand<Result<InventoryReservationReadModel>>;
