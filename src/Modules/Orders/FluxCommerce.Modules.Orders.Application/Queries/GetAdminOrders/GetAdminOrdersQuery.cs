using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;

/// <summary>
/// Administrative Order list: all customers, newest first, with optional filters.
/// Authorization is enforced at the API boundary (<c>AdminPolicy</c>); the handler
/// performs no ownership scoping. Status is the <c>OrderStatus</c> name
/// (<c>Pending|Cancelled|Confirmed</c>, case-insensitive); search matches
/// <c>OrderNumber</c> substring.
/// </summary>
public sealed record GetAdminOrdersQuery(
    int? PageNumber,
    int? PageSize,
    string? Status,
    Guid? CustomerId,
    string? Search,
    DateTimeOffset? From,
    DateTimeOffset? To)
    : IQuery<Result<PagedResult<OrderSummaryResponse>>>;
