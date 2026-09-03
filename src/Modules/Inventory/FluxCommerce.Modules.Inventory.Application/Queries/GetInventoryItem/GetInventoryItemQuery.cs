using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;

/// <summary>
/// Reads the current stock state (on-hand, reserved and available quantities) of a single SKU.
/// </summary>
public sealed record GetInventoryItemQuery(string Sku)
    : IQuery<Result<InventoryItemReadModel>>;
