using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;

/// <summary>
/// Initializes a new inventory item for the given SKU. An initial on-hand quantity of zero is valid
/// (the SKU can be registered before stock arrives); negative quantities are rejected.
/// </summary>
public sealed record CreateInventoryItemCommand(string Sku, int OnHandQuantity)
    : ICommand<Result<InventoryItemReadModel>>;
