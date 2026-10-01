using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;

/// <summary>
/// Returns aggregate user counts (by status, by role, unverified and currently locked out)
/// for the administrative overview.
/// </summary>
public sealed record GetAdminUserCountsQuery
    : IQuery<Result<UserAdminCountsResponse>>;
