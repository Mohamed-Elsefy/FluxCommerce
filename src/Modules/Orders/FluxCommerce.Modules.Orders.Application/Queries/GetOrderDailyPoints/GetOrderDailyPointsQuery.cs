using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.ReadModels;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;

/// <summary>
/// Daily-series projection for Orders: lightweight creation timestamps within the trailing
/// window. <c>Days</c> is validated by the caller (dashboard contract: 1..90); values below 1
/// are rejected here as a defense-in-depth guard. Authorization is enforced at the API boundary.
/// </summary>
public sealed record GetOrderDailyPointsQuery(
    int Days)
    : IQuery<Result<IReadOnlyList<OrderDailyPointReadModel>>>;
