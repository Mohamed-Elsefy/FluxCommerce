using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetOrder;

/// <summary>
/// Reads a single Order owned by the authenticated customer.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId)
    : IQuery<Result<OrderDetailsResponse>>;
