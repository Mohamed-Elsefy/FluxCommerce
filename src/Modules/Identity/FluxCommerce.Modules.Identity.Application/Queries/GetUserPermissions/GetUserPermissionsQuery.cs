using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetUserPermissions;

/// <summary>
/// Reads the action-level permissions granted to a user. The caller must be authorized at
/// the API boundary (<c>SuperAdminPolicy</c>).
/// </summary>
public sealed record GetUserPermissionsQuery(Guid UserId)
    : IQuery<Result<IReadOnlyCollection<string>>>;
