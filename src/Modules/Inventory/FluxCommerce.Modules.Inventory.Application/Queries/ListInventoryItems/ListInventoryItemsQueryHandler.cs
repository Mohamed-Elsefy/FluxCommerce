using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Application.Validation;

namespace FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryItems;

/// <summary>
/// Administrative inventory item list: validates pagination and reads from the
/// lightweight projection without tracking aggregates.
/// </summary>
public sealed class ListInventoryItemsQueryHandler
    : IQueryHandler<ListInventoryItemsQuery, Result<PagedResult<InventoryItemReadModel>>>
{
    private readonly IInventoryStore _inventoryStore;

    public ListInventoryItemsQueryHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
    }

    public async Task<Result<PagedResult<InventoryItemReadModel>>> HandleAsync(
        ListInventoryItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = query.PageNumber ?? InventoryPaginationRules.DefaultPageNumber;
        var pageSize = query.PageSize ?? InventoryPaginationRules.DefaultPageSize;

        var validationError = InventoryPaginationRules.Validate(pageNumber, pageSize);
        if (validationError is not null)
            return Result.Failure<PagedResult<InventoryItemReadModel>>(validationError);

        if (query.MaxAvailable.HasValue && query.MaxAvailable.Value < 0)
            return Result.Failure<PagedResult<InventoryItemReadModel>>(InventoryApplicationErrors.ValidationFailed);

        var result = await _inventoryStore
            .GetItemsPageAsync(
                query.Search,
                query.MaxAvailable,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(result);
    }
}
