using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;
using FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryReservation;
using FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryItems;
using FluxCommerce.Modules.Inventory.Application.Queries.ListInventoryReservations;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.Requests;
using FluxCommerce.Modules.Inventory.Contracts.Responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DomainInventoryReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;

namespace FluxCommerce.Api.Controllers.Inventory;

/// <summary>
/// Administrative inventory surface: stock initialization, inspection, additive adjustments and
/// operational reservation lookup. All endpoints require the Admin role; reservation ids are never
/// enumerable and the role gate prevents customer-level IDOR on reservation state.
/// </summary>
[ApiController]
[Route("inventory")]
[Tags(EndpointTags.Inventory)]
[Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
public sealed class InventoryController : ApiControllerBase
{
    private readonly ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>> _createItemHandler;
    private readonly ICommandHandler<AdjustStockCommand, Result<InventoryItemReadModel>> _adjustStockHandler;
    private readonly IQueryHandler<GetInventoryItemQuery, Result<InventoryItemReadModel>> _getItemHandler;
    private readonly IQueryHandler<GetInventoryReservationQuery, Result<InventoryReservationReadModel>> _getReservationHandler;
    private readonly IQueryHandler<ListInventoryItemsQuery, Result<PagedResult<InventoryItemReadModel>>> _listItemsHandler;
    private readonly IQueryHandler<ListInventoryReservationsQuery, Result<PagedResult<InventoryReservationReadModel>>> _listReservationsHandler;
    private readonly IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> _getCountsHandler;

    public InventoryController(
        ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>> createItemHandler,
        ICommandHandler<AdjustStockCommand, Result<InventoryItemReadModel>> adjustStockHandler,
        IQueryHandler<GetInventoryItemQuery, Result<InventoryItemReadModel>> getItemHandler,
        IQueryHandler<GetInventoryReservationQuery, Result<InventoryReservationReadModel>> getReservationHandler,
        IQueryHandler<ListInventoryItemsQuery, Result<PagedResult<InventoryItemReadModel>>> listItemsHandler,
        IQueryHandler<ListInventoryReservationsQuery, Result<PagedResult<InventoryReservationReadModel>>> listReservationsHandler,
        IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> getCountsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createItemHandler = createItemHandler;
        _adjustStockHandler = adjustStockHandler;
        _getItemHandler = getItemHandler;
        _getReservationHandler = getReservationHandler;
        _listItemsHandler = listItemsHandler;
        _listReservationsHandler = listReservationsHandler;
        _getCountsHandler = getCountsHandler;
    }

    /// <summary>
    /// Initializes an inventory item for a SKU that does not already exist. A duplicate SKU is a
    /// conflict; the SKU is normalized (trimmed, uppercased) before it is stored.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<InventoryItemResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var result = await _createItemHandler.HandleAsync(
            new CreateInventoryItemCommand(request.Sku, request.OnHandQuantity),
            cancellationToken);

        var mapped = Map(result);

        if (mapped.IsFailure)
            return Created<InventoryItemResponse>(mapped);

        var body = mapped.Value!;
        return CreatedAtAction(
            nameof(Get),
            new { sku = body.Sku },
            ApiResponse<InventoryItemResponse>.Create(body));
    }

    /// <summary>
    /// Returns the current stock state (on-hand, reserved and available quantities) of a single SKU.
    /// </summary>
    [HttpGet("{sku}")]
    [ProducesResponseType(typeof(ApiResponse<InventoryItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string sku, CancellationToken cancellationToken)
    {
        var result = await _getItemHandler.HandleAsync(
            new GetInventoryItemQuery(sku),
            cancellationToken);

        return Success(Map(result));
    }

    /// <summary>
    /// Adds stock to an existing inventory item. Adjustment is additive only: stock consumption is
    /// modeled by the reservation lifecycle (commit), so there is no unsafe manual decrement path.
    /// </summary>
    [HttpPatch("{sku}")]
    [ProducesResponseType(typeof(ApiResponse<InventoryItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdjustStock(string sku, [FromBody] AdjustStockRequest request, CancellationToken cancellationToken)
    {
        var result = await _adjustStockHandler.HandleAsync(
            new AdjustStockCommand(sku, request.Quantity),
            cancellationToken);

        return Success(Map(result));
    }

    /// <summary>
    /// Lists inventory items ordered by SKU with optional substring search and an optional
    /// available-quantity ceiling. Pagination falls back to page 1 with a page size of 20
    /// (maximum 100).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<InventoryItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] int? maxAvailable,
        CancellationToken cancellationToken)
    {
        var result = await _listItemsHandler.HandleAsync(
            new ListInventoryItemsQuery(pageNumber, pageSize, search, maxAvailable),
            cancellationToken);

        if (result.IsFailure)
            return Success(Result<PagedResult<InventoryItemResponse>>.Failure(result.Error));

        var page = result.Value!;
        return Success(Result.Success(new PagedResult<InventoryItemResponse>(
            page.Items.Select(ToItemResponse).ToList(),
            page.TotalCount,
            page.PageNumber,
            page.PageSize)));
    }

    /// <summary>
    /// Returns aggregate inventory counts (item totals and reservation counts by status)
    /// for the administrative overview.
    /// </summary>
    [HttpGet("counts")]
    [ProducesResponseType(typeof(ApiResponse<InventoryCountsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCounts(
        [FromQuery] int? lowStockThreshold,
        CancellationToken cancellationToken)
    {
        var result = await _getCountsHandler.HandleAsync(
            new GetInventoryCountsQuery(lowStockThreshold),
            cancellationToken);

        if (result.IsFailure)
            return Success(Result<InventoryCountsResponse>.Failure(result.Error));

        var counts = result.Value!;
        return Success(Result.Success(new InventoryCountsResponse(
            counts.TotalItems,
            counts.TotalOnHand,
            counts.TotalReserved,
            counts.LowStockItems,
            counts.PendingReservations,
            counts.CommittedReservations,
            counts.ReleasedReservations,
            counts.ExpiredReservations)));
    }

    /// <summary>
    /// Lists reservations, newest first, with optional status, SKU and order filters.
    /// Pagination falls back to page 1 with a page size of 20 (maximum 100).
    /// </summary>
    [HttpGet("reservations")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<InventoryReservationResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListReservations(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] string? sku,
        [FromQuery] Guid? orderId,
        CancellationToken cancellationToken)
    {
        var result = await _listReservationsHandler.HandleAsync(
            new ListInventoryReservationsQuery(pageNumber, pageSize, status, sku, orderId),
            cancellationToken);

        if (result.IsFailure)
            return Success(Result<PagedResult<InventoryReservationResponse>>.Failure(result.Error));

        var page = result.Value!;
        return Success(Result.Success(new PagedResult<InventoryReservationResponse>(
            page.Items.Select(ToReservationResponse).ToList(),
            page.TotalCount,
            page.PageNumber,
            page.PageSize)));
    }

    /// <summary>
    /// Returns the current state of a single reservation by its identifier. This is an operational
    /// (administrative) read; reservation ids are never enumerable and the endpoint is authenticated.
    /// </summary>
    [HttpGet("reservations/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InventoryReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReservation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getReservationHandler.HandleAsync(
            new GetInventoryReservationQuery(id),
            cancellationToken);

        return Success(Map(result));
    }

    private static Result<InventoryItemResponse> Map(Result<InventoryItemReadModel> result)
    {
        if (result.IsFailure)
            return Result<InventoryItemResponse>.Failure(result.Error);

        return Result<InventoryItemResponse>.Success(ToItemResponse(result.Value!));
    }

    private static InventoryItemResponse ToItemResponse(InventoryItemReadModel item) =>
        new(
            item.Sku,
            item.OnHandQuantity,
            item.ReservedQuantity,
            item.AvailableQuantity,
            item.UpdatedAtUtc);

    private static Result<InventoryReservationResponse> Map(Result<InventoryReservationReadModel> result)
    {
        if (result.IsFailure)
            return Result<InventoryReservationResponse>.Failure(result.Error);

        return Result<InventoryReservationResponse>.Success(ToReservationResponse(result.Value!));
    }

    private static InventoryReservationResponse ToReservationResponse(InventoryReservationReadModel reservation) =>
        new(
            reservation.ReservationId,
            reservation.OrderId,
            reservation.Sku,
            reservation.Quantity,
            ToContractStatus(reservation.Status),
            reservation.ExpiresAt,
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc);

    private static InventoryReservationStatus ToContractStatus(DomainInventoryReservationStatus status) =>
        status switch
        {
            DomainInventoryReservationStatus.Pending => InventoryReservationStatus.Pending,
            DomainInventoryReservationStatus.Committed => InventoryReservationStatus.Committed,
            DomainInventoryReservationStatus.Released => InventoryReservationStatus.Released,
            DomainInventoryReservationStatus.Expired => InventoryReservationStatus.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
