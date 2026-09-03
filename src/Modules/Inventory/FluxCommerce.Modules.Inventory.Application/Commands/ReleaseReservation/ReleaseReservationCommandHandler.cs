using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;

/// <summary>
/// Loads the reservation and its inventory item, then delegates the release rules to the Domain.
/// Releasing an already-terminal reservation is rejected as a conflict and nothing is persisted, so a
/// repeated release can never double-return reserved quantity. When a concurrent lifecycle operation
/// wins the optimistic-concurrency race, the losing attempt surfaces as a conflict error instead of
/// an exception.
/// </summary>
public sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ReleaseReservationCommandHandler(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<InventoryReservationReadModel>> HandleAsync(
        ReleaseReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        InventoryReservationId reservationId;
        try
        {
            reservationId = new InventoryReservationId(command.ReservationId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InvalidReservationId);
        }

        var reservation = await _inventoryStore
            .GetReservationByIdAsync(reservationId, cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null)
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.ReservationNotFound);

        var item = await _inventoryStore
            .GetItemBySkuAsync(reservation.Sku, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
            return Result.Failure<InventoryReservationReadModel>(InventoryApplicationErrors.SkuNotFound);

        var release = reservation.Release(
            item,
            new DateTimeOffset(_dateTimeProvider.UtcNow));

        if (release.IsFailure)
            return Result.Failure<InventoryReservationReadModel>(
                InventoryReservationErrorMapper.Map(release.Error));

        if (!await _inventoryStore
                .SaveLifecycleChangesAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            // A concurrent commit/release/expiration already moved the reservation; the losing
            // attempt must not be reported as a success.
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InvalidStateTransition);
        }

        return Result.Success(InventoryReservationReadModelMapper.ToReadModel(reservation));
    }
}
