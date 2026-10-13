using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;

/// <summary>
/// Returns aggregate Inventory counts for the administrative overview.
/// </summary>
public sealed class GetInventoryCountsQueryHandler
    : IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>>
{
    public const int DefaultLowStockThreshold = 5;

    private readonly IInventoryStore _inventoryStore;

    public GetInventoryCountsQueryHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
    }

    public async Task<Result<InventoryCountsReadModel>> HandleAsync(
        GetInventoryCountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var threshold = query.LowStockThreshold ?? DefaultLowStockThreshold;

        if (threshold < 0)
            return Result.Failure<InventoryCountsReadModel>(InventoryApplicationErrors.ValidationFailed);

        var result = await _inventoryStore
            .GetCountsAsync(threshold, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(result);
    }
}
