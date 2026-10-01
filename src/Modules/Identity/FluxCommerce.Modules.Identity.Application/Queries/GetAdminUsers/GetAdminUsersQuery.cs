using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;

/// <summary>
/// Administrative user list, newest first, with optional status, role and text-search
/// filters. Status is the <c>UserStatus</c> name (<c>Pending|Active|Suspended|Locked|Deleted</c>,
/// case-insensitive); role is one of <c>Customer|Admin|SuperAdmin</c>; search matches
/// email, first name or last name substrings.
/// </summary>
public sealed record GetAdminUsersQuery(
    int? PageNumber,
    int? PageSize,
    string? Status,
    string? Role,
    string? Search)
    : IQuery<Result<PagedResult<UserAdminResponse>>>;
