using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.RevokePermission;

/// <summary>
/// Revokes one action-level permission from any user. Revoking an absent permission is a
/// benign no-op. The caller must be authorized at the API boundary (<c>SuperAdminPolicy</c>).
/// The change takes effect on the user's next login/refresh.
/// </summary>
public sealed record RevokePermissionCommand(Guid UserId, string Permission)
    : ICommand<Result<UserResponse>>;
