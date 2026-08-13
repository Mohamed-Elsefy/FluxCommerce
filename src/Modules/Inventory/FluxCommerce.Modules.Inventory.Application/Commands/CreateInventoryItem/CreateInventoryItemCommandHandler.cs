using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;

/// <summary>
/// Creates an inventory item for a SKU that does not already exist. The SKU is normalized by the
/// <see cref="InventorySku"/> value object (trimmed, uppercased, format-checked); a duplicate SKU is
/// rejected as a conflict so initialization stays idempotent at the application level.
/// </summary>
public sealed class CreateInventoryItemCommandHandler : ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateInventoryItemCommandHandler(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<InventoryItemReadModel>> HandleAsync(
        CreateInventoryItemCommand command,
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

        if (command.OnHandQuantity < 0)
            return Result.Failure<InventoryItemReadModel>(InventoryApplicationErrors.InvalidQuantity);

        if (await _inventoryStore
                .GetItemBySkuAsync(sku, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return Result.Failure<InventoryItemReadModel>(InventoryApplicationErrors.DuplicateSku);
        }

        var item = InventoryItem.Create(
            sku,
            command.OnHandQuantity,
            new DateTimeOffset(_dateTimeProvider.UtcNow));

        await _inventoryStore.AddItemAsync(item, cancellationToken).ConfigureAwait(false);
        await _inventoryStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(InventoryItemReadModelMapper.ToReadModel(item));
    }
}
