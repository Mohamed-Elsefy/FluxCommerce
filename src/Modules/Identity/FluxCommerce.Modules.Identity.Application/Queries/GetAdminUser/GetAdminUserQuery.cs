using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUser;

/// <summary>
/// Reads a single user by identity for administration, including lockout visibility.
/// </summary>
public sealed record GetAdminUserQuery(Guid UserId)
    : IQuery<Result<UserAdminResponse>>;
