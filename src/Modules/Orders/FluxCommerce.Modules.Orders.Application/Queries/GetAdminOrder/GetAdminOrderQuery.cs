using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrder;

/// <summary>
/// Reads a single Order by identity without customer ownership scoping.
/// The caller must be authorized at the API boundary (<c>AdminPolicy</c>).
/// </summary>
public sealed record GetAdminOrderQuery(Guid OrderId)
    : IQuery<Result<OrderDetailsResponse>>;
