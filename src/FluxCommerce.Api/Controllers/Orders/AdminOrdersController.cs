using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrder;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Orders;

/// <summary>
/// Administrative Orders surface: full Order list with filters, single Order inspection
/// and aggregate counts. All endpoints require the Admin role; no customer ownership
/// scoping is applied.
/// </summary>
[ApiController]
[Route("admin/orders")]
[Tags(EndpointTags.Orders)]
[Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
public sealed class AdminOrdersController : ApiControllerBase
{
    private readonly IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _getAdminOrdersHandler;
    private readonly IQueryHandler<GetAdminOrderQuery, Result<OrderDetailsResponse>> _getAdminOrderHandler;
    private readonly IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> _getAdminOrderCountsHandler;

    public AdminOrdersController(
        IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> getAdminOrdersHandler,
        IQueryHandler<GetAdminOrderQuery, Result<OrderDetailsResponse>> getAdminOrderHandler,
        IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> getAdminOrderCountsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _getAdminOrdersHandler = getAdminOrdersHandler;
        _getAdminOrderHandler = getAdminOrderHandler;
        _getAdminOrderCountsHandler = getAdminOrderCountsHandler;
    }

    /// <summary>
    /// Lists all Orders across customers, newest first, with optional status, customer,
    /// order-number search and creation-date filters. Pagination falls back to page 1
    /// with a page size of 20 (maximum 100).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAdminOrders(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] string? search,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var result = await _getAdminOrdersHandler.HandleAsync(
            new GetAdminOrdersQuery(pageNumber, pageSize, status, customerId, search, from, to),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Returns aggregate Order counts grouped by status for the administrative overview.
    /// </summary>
    [HttpGet("counts")]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderCountsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminOrderCounts(CancellationToken cancellationToken)
    {
        var result = await _getAdminOrderCountsHandler.HandleAsync(
            new GetAdminOrderCountsQuery(),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Retrieves a single Order by identity without customer ownership scoping,
    /// including its line items and totals.
    /// </summary>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdminOrder(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await _getAdminOrderHandler.HandleAsync(
            new GetAdminOrderQuery(orderId),
            cancellationToken);

        return Success(result);
    }
}
