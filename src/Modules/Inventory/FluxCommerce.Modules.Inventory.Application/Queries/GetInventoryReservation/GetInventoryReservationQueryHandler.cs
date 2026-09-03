using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryReservation;

/// <summary>
/// Loads the requested reservation by its identifier. A missing or invalid identifier surfaces as a
/// not-found/validation error respectively.
/// </summary>
public sealed class GetInventoryReservationQueryHandler
    : IQueryHandler<GetInventoryReservationQuery, Result<InventoryReservationReadModel>>
{
    private readonly IInventoryStore _inventoryStore;

    public GetInventoryReservationQueryHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
    }

    public async Task<Result<InventoryReservationReadModel>> HandleAsync(
        GetInventoryReservationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ReservationId == Guid.Empty)
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.InvalidReservationId);

        var reservation = await _inventoryStore
            .GetReservationByIdAsync(new InventoryReservationId(query.ReservationId), cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null)
            return Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.ReservationNotFound);

        return Result.Success(InventoryReservationReadModelMapper.ToReadModel(reservation));
    }
}
