using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;
using FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrder;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Aggregates;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Orders;

[ApiController]
[Route("orders")]
[Tags(EndpointTags.Orders)]
[Authorize]
public sealed class OrdersController : ApiControllerBase
{
    private readonly ICommandHandler<CheckoutCommand, Result<Order>> _checkoutHandler;
    private readonly IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _getMyOrdersHandler;
    private readonly IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>> _getOrderHandler;
    private readonly ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>> _cancelOrderHandler;

    public OrdersController(
        ICommandHandler<CheckoutCommand, Result<Order>> checkoutHandler,
        IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> getMyOrdersHandler,
        IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>> getOrderHandler,
        ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>> cancelOrderHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _checkoutHandler = checkoutHandler;
        _getMyOrdersHandler = getMyOrdersHandler;
        _getOrderHandler = getOrderHandler;
        _cancelOrderHandler = cancelOrderHandler;
    }

    /// <summary>
    /// Places an irreversible Checkout Order snapshot for the authenticated customer's Cart. The
    /// order is built from the current revalidated Catalog data and persisted atomically. Repeating
    /// the same request with the same <c>Idempotency-Key</c> header returns the original Order
    /// instead of creating a duplicate.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(ApiResponse<CheckoutResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new CheckoutCommand(
            request.CartId,
            idempotencyKey ?? string.Empty,
            Map(request.ShippingAddress),
            Map(request.BillingAddress));

        var result = await _checkoutHandler.HandleAsync(command, cancellationToken);

        return Success(MapToResponse(result));
    }

    /// <summary>
    /// Lists the authenticated customer's Orders, newest first, with an optional status filter.
    /// Pagination is optional and falls back to page 1 with a page size of 20 (maximum 100).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderSummaryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await _getMyOrdersHandler.HandleAsync(
            new GetMyOrdersQuery(pageNumber, pageSize, status),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Retrieves a single Order owned by the authenticated customer, including its line items and
    /// totals.
    /// </summary>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await _getOrderHandler.HandleAsync(
            new GetOrderQuery(orderId),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Cancels an Order owned by the authenticated customer while it is still Pending. Finalized
    /// Orders are rejected with a conflict.
    /// </summary>
    [HttpPost("{orderId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await _cancelOrderHandler.HandleAsync(
            new CancelOrderCommand(orderId),
            cancellationToken);

        return Success(result);
    }

    private static CheckoutAddressData Map(CheckoutAddressRequest address) =>
        new(
            address.RecipientName,
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode);

    private static Result<CheckoutResponse> MapToResponse(Result<Order> result)
    {
        if (result.IsFailure)
            return Result<CheckoutResponse>.Failure(result.Error);

        var order = result.Value!;
        var response = new CheckoutResponse(
            order.Id.Value,
            order.OrderNumber.Value,
            order.Status.ToString(),
            order.CreatedAtUtc);

        return Result<CheckoutResponse>.Success(response);
    }
}
