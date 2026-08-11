using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;

/// <summary>
/// Lists the authenticated customer's Orders, newest first. Pagination is optional: missing values
/// fall back to the module defaults.
/// </summary>
public sealed record GetMyOrdersQuery(int? PageNumber, int? PageSize)
    : IQuery<Result<PagedResult<OrderSummaryResponse>>>;
