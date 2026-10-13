using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;

/// <summary>
/// Lists the authenticated customer's Orders, newest first. Pagination is optional and falls back
/// to the module defaults. Status is the <c>OrderStatus</c> name
/// (<c>Pending|Cancelled|Confirmed</c>, case-insensitive) and narrows the page when supplied.
/// </summary>
public sealed record GetMyOrdersQuery(int? PageNumber, int? PageSize, string? Status = null)
    : IQuery<Result<PagedResult<OrderSummaryResponse>>>;
