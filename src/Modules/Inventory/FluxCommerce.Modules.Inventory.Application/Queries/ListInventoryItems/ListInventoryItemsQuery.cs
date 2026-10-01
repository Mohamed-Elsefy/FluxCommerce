using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryItems;

/// <summary>
/// Administrative inventory item list ordered by SKU with optional substring search and
/// an optional available-quantity ceiling (<c>MaxAvailable</c>).
/// </summary>
public sealed record ListInventoryItemsQuery(
    int? PageNumber,
    int? PageSize,
    string? Search,
    int? MaxAvailable)
    : IQuery<Result<PagedResult<InventoryItemReadModel>>>;
