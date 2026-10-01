using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;

/// <summary>
/// Returns aggregate Inventory counts for the administrative overview.
/// <c>LowStockThreshold</c> is the inclusive available-quantity ceiling for low-stock
/// items (defaults to <see cref="GetInventoryCountsQueryHandler.DefaultLowStockThreshold"/>).
/// </summary>
public sealed record GetInventoryCountsQuery(int? LowStockThreshold)
    : IQuery<Result<InventoryCountsReadModel>>;
