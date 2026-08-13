using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;

/// <summary>
/// Reserves stock through the store's atomic conditional reservation. The stock check and its
/// mutation are a single database UPDATE guarded by availability, so concurrent reservations cannot
/// oversell; an insufficient-stock attempt fails with a conflict and leaves inventory unchanged.
/// Input validation (SKU, order id, quantity, expiration) happens up front so invalid requests are
/// rejected without touching the database.
/// </summary>
public sealed class ReserveInventoryCommandHandler : ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ReserveInventoryCommandHandler(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<InventoryReservationReadModel>> HandleAsync(
        ReserveInventoryCommand command,
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
            return Result.Failure<InventoryReservationReadModel>(InventoryApplicationErrors.InvalidSku);
        }

        OrderId orderId;
        try
        {
            orderId = new OrderId(command.OrderId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InvalidOrderId);
        }

        var utcNow = new DateTimeOffset(_dateTimeProvider.UtcNow);

        if (command.Quantity < 1)
            return Result.Failure<InventoryReservationReadModel>(InventoryApplicationErrors.InvalidQuantity);

        if (command.ExpiresAt <= utcNow)
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InvalidExpiration);

        if (!await _inventoryStore
                .ExistsAsync(sku, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure<InventoryReservationReadModel>(InventoryApplicationErrors.SkuNotFound);
        }

        var reservation = await _inventoryStore
            .TryReserveAsync(
                sku,
                InventoryReservationId.Create(),
                orderId,
                command.Quantity,
                command.ExpiresAt,
                utcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null)
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InsufficientStock);

        return Result.Success(InventoryReservationReadModelMapper.ToReadModel(reservation));
    }
}
