using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.ReadModels;

namespace FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryReservations;

/// <summary>
/// Administrative reservation list, newest first, with optional status, SKU and order
/// filters. Status is the reservation status name
/// (<c>Pending|Committed|Released|Expired</c>, case-insensitive).
/// </summary>
public sealed record ListInventoryReservationsQuery(
    int? PageNumber,
    int? PageSize,
    string? Status,
    string? Sku,
    Guid? OrderId)
    : IQuery<Result<PagedResult<InventoryReservationReadModel>>>;
