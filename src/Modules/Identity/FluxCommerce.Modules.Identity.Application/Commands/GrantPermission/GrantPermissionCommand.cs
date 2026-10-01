using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.GrantPermission;

/// <summary>
/// Grants one action-level permission to any user. The caller must be authorized at the API
/// boundary (<c>SuperAdminPolicy</c>). The grant takes effect on the user's next login/refresh,
/// when a fresh access token is issued.
/// </summary>
public sealed record GrantPermissionCommand(Guid UserId, string Permission)
    : ICommand<Result<UserResponse>>;
