using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;

/// <summary>
/// Loads the requested inventory item by SKU. A missing SKU surfaces as a not-found error.
/// </summary>
public sealed class GetInventoryItemQueryHandler : IQueryHandler<GetInventoryItemQuery, Result<InventoryItemReadModel>>
{
    private readonly IInventoryStore _inventoryStore;

    public GetInventoryItemQueryHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
    }

    public async Task<Result<InventoryItemReadModel>> HandleAsync(
        GetInventoryItemQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        InventorySku sku;
        try
        {
            sku = new InventorySku(query.Sku);
        }
        catch (ArgumentException)
        {
            return Result.Failure<InventoryItemReadModel>(InventoryApplicationErrors.InvalidSku);
        }

        var item = await _inventoryStore
            .GetItemBySkuAsync(sku, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
            return Result.Failure<InventoryItemReadModel>(InventoryApplicationErrors.SkuNotFound);

        return Result.Success(InventoryItemReadModelMapper.ToReadModel(item));
    }
}
