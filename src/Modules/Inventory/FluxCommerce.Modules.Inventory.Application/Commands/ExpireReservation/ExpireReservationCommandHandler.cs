using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Commands.ExpireReservation;

/// <summary>
/// Loads the reservation and its inventory item, then delegates the expiration rules to the Domain.
/// An expired reservation releases its reserved quantity and becomes terminal; already-released,
/// already-committed or already-expired reservations are rejected as a conflict so a repeated
/// expiration can never double-release quantity. When a concurrent lifecycle operation wins the
/// optimistic-concurrency race, the losing attempt surfaces as a conflict error instead of an
/// exception. Background expiration processing is out of scope for this use case.
/// </summary>
public sealed class ExpireReservationCommandHandler : ICommandHandler<ExpireReservationCommand, Result<InventoryReservationReadModel>>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ExpireReservationCommandHandler(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<InventoryReservationReadModel>> HandleAsync(
        ExpireReservationCommand command,
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

        var expire = reservation.Expire(
            item,
            new DateTimeOffset(_dateTimeProvider.UtcNow));

        if (expire.IsFailure)
            return Result.Failure<InventoryReservationReadModel>(
                InventoryReservationErrorMapper.Map(expire.Error));

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
