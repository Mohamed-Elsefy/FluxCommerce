using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;

/// <summary>
/// Adds the requested quantity to the on-hand stock of an existing item. A missing SKU is a
/// not-found error; a non-positive quantity is rejected by the domain invariant and surfaced as a
/// validation error without persisting.
/// </summary>
public sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand, Result<InventoryItemReadModel>>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AdjustStockCommandHandler(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<InventoryItemReadModel>> HandleAsync(
        AdjustStockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        InventorySku sku;
        try
        {
            sku = new InventorySku(command.Sku);
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

        var adjustment = item.AddStock(
            command.Quantity,
            new DateTimeOffset(_dateTimeProvider.UtcNow));

        if (adjustment.IsFailure)
            return Result.Failure<InventoryItemReadModel>(InventoryApplicationErrors.InvalidQuantity);

        await _inventoryStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(InventoryItemReadModelMapper.ToReadModel(item));
    }
}
