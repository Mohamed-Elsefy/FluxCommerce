using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;

/// <summary>
/// Returns aggregate Order counts grouped by status for the administrative overview.
/// </summary>
public sealed record GetAdminOrderCountsQuery
    : IQuery<Result<AdminOrderCountsResponse>>;
