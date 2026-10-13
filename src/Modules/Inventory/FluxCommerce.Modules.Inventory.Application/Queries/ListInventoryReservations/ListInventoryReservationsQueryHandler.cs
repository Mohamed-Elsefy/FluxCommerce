using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Application.Validation;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryReservations;

/// <summary>
/// Administrative reservation list: validates pagination and the optional filters,
/// then reads from the lightweight projection without tracking aggregates.
/// </summary>
public sealed class ListInventoryReservationsQueryHandler
    : IQueryHandler<ListInventoryReservationsQuery, Result<PagedResult<InventoryReservationReadModel>>>
{
    private readonly IInventoryStore _inventoryStore;

    public ListInventoryReservationsQueryHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
    }

    public async Task<Result<PagedResult<InventoryReservationReadModel>>> HandleAsync(
        ListInventoryReservationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = query.PageNumber ?? InventoryPaginationRules.DefaultPageNumber;
        var pageSize = query.PageSize ?? InventoryPaginationRules.DefaultPageSize;

        var validationError = InventoryPaginationRules.Validate(pageNumber, pageSize);
        if (validationError is not null)
            return Result.Failure<PagedResult<InventoryReservationReadModel>>(validationError);

        InventoryReservationStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<InventoryReservationStatus>(query.Status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(InventoryReservationStatus), parsed))
                return Result.Failure<PagedResult<InventoryReservationReadModel>>(InventoryApplicationErrors.ValidationFailed);

            status = parsed;
        }

        InventorySku? sku = null;
        if (!string.IsNullOrWhiteSpace(query.Sku))
        {
            try
            {
                sku = new InventorySku(query.Sku);
            }
            catch (ArgumentException)
            {
                return Result.Failure<PagedResult<InventoryReservationReadModel>>(InventoryApplicationErrors.InvalidSku);
            }
        }

        OrderId? orderId = null;
        if (query.OrderId.HasValue)
        {
            if (query.OrderId.Value == Guid.Empty)
                return Result.Failure<PagedResult<InventoryReservationReadModel>>(InventoryReservationErrors.InvalidOrderId);

            orderId = new OrderId(query.OrderId.Value);
        }

        var result = await _inventoryStore
            .GetReservationsPageAsync(
                status,
                sku,
                orderId,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(result);
    }
}
