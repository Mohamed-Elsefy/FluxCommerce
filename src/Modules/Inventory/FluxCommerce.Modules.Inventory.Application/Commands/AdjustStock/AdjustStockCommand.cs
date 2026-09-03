using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;

/// <summary>
/// Adds stock to an existing inventory item. Adjustment is additive only: stock consumption is
/// modeled by the reservation lifecycle (commit), so there is no unsafe manual decrement path.
/// </summary>
public sealed record AdjustStockCommand(string Sku, int Quantity)
    : ICommand<Result<InventoryItemReadModel>>;
