using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Commands.AssignRole;
using FluxCommerce.Modules.Identity.Application.Commands.GrantPermission;
using FluxCommerce.Modules.Identity.Application.Commands.RevokePermission;
using FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUser;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;
using FluxCommerce.Modules.Identity.Application.Queries.GetPermissionCatalog;
using FluxCommerce.Modules.Identity.Application.Queries.GetUserPermissions;
using FluxCommerce.Modules.Identity.Contracts.Requests;
using FluxCommerce.Modules.Identity.Contracts.Responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Identity;

[ApiController]
[Route("admin/users")]
[Authorize]
[Tags(EndpointTags.Identity)]
public sealed class AdminController : ApiControllerBase
{
    private readonly ICommandHandler<AssignRoleCommand, Result<UserResponse>> _assignRoleHandler;
    private readonly ICommandHandler<UnlockUserCommand, Result<UserResponse>> _unlockUserHandler;
    private readonly IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> _getAdminUsersHandler;
    private readonly IQueryHandler<GetAdminUserQuery, Result<UserAdminResponse>> _getAdminUserHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _getAdminUserCountsHandler;
    private readonly ICommandHandler<GrantPermissionCommand, Result<UserResponse>> _grantPermissionHandler;
    private readonly ICommandHandler<RevokePermissionCommand, Result<UserResponse>> _revokePermissionHandler;
    private readonly IQueryHandler<GetUserPermissionsQuery, Result<IReadOnlyCollection<string>>> _getUserPermissionsHandler;
    private readonly IQueryHandler<GetPermissionCatalogQuery, Result<IReadOnlyCollection<string>>> _getPermissionCatalogHandler;

    public AdminController(
        ICommandHandler<AssignRoleCommand, Result<UserResponse>> assignRoleHandler,
        ICommandHandler<UnlockUserCommand, Result<UserResponse>> unlockUserHandler,
        IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> getAdminUsersHandler,
        IQueryHandler<GetAdminUserQuery, Result<UserAdminResponse>> getAdminUserHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> getAdminUserCountsHandler,
        ICommandHandler<GrantPermissionCommand, Result<UserResponse>> grantPermissionHandler,
        ICommandHandler<RevokePermissionCommand, Result<UserResponse>> revokePermissionHandler,
        IQueryHandler<GetUserPermissionsQuery, Result<IReadOnlyCollection<string>>> getUserPermissionsHandler,
        IQueryHandler<GetPermissionCatalogQuery, Result<IReadOnlyCollection<string>>> getPermissionCatalogHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _assignRoleHandler = assignRoleHandler;
        _unlockUserHandler = unlockUserHandler;
        _getAdminUsersHandler = getAdminUsersHandler;
        _getAdminUserHandler = getAdminUserHandler;
        _getAdminUserCountsHandler = getAdminUserCountsHandler;
        _grantPermissionHandler = grantPermissionHandler;
        _revokePermissionHandler = revokePermissionHandler;
        _getUserPermissionsHandler = getUserPermissionsHandler;
        _getPermissionCatalogHandler = getPermissionCatalogHandler;
    }

    /// <summary>Assigns a role to a user. Requires user-management rights.</summary>
    [HttpPost("{id:guid}/roles")]
    [Authorize(Policy = AuthenticationConfiguration.UsersManagePolicy)]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var command = new AssignRoleCommand(id, request.Role);
        return Success(await _assignRoleHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Unlocks a locked user account. Requires user-management rights.</summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = AuthenticationConfiguration.UsersManagePolicy)]
    public async Task<IActionResult> UnlockUser(Guid id, CancellationToken cancellationToken)
    {
        var command = new UnlockUserCommand(id);
        return Success(await _unlockUserHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>
    /// Lists users, newest first, with optional status, role and text-search filters.
    /// Pagination falls back to page 1 with a page size of 20 (maximum 100).
    /// Requires user-read rights.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthenticationConfiguration.UsersReadPolicy)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserAdminResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] string? role,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Success(await _getAdminUsersHandler.HandleAsync(
            new GetAdminUsersQuery(pageNumber, pageSize, status, role, search),
            cancellationToken));
    }

    /// <summary>
    /// Returns aggregate user counts (by status, by role, unverified and currently
    /// locked out) for the administrative overview. Requires user-read rights.
    /// </summary>
    [HttpGet("counts")]
    [Authorize(Policy = AuthenticationConfiguration.UsersReadPolicy)]
    [ProducesResponseType(typeof(ApiResponse<UserAdminCountsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserCounts(CancellationToken cancellationToken)
    {
        return Success(await _getAdminUserCountsHandler.HandleAsync(
            new GetAdminUserCountsQuery(),
            cancellationToken));
    }

    /// <summary>
    /// Retrieves a single user by identity, including lockout visibility.
    /// Requires user-read rights.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.UsersReadPolicy)]
    [ProducesResponseType(typeof(ApiResponse<UserAdminResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        return Success(await _getAdminUserHandler.HandleAsync(
            new GetAdminUserQuery(id),
            cancellationToken));
    }

    /// <summary>
    /// Grants one action-level permission to a user. Takes effect on the user's next
    /// login/refresh. Requires user-management rights.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [Authorize(Policy = AuthenticationConfiguration.UsersManagePolicy)]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantPermission(
        Guid id,
        [FromBody] GrantPermissionRequest request,
        CancellationToken cancellationToken)
    {
        return Success(await _grantPermissionHandler.HandleAsync(
            new GrantPermissionCommand(id, request.Permission),
            cancellationToken));
    }

    /// <summary>
    /// Revokes one action-level permission from a user. Revoking an absent permission is a
    /// benign no-op. Requires user-management rights.
    /// </summary>
    [HttpDelete("{id:guid}/permissions/{permission}")]
    [Authorize(Policy = AuthenticationConfiguration.UsersManagePolicy)]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokePermission(
        Guid id,
        string permission,
        CancellationToken cancellationToken)
    {
        return Success(await _revokePermissionHandler.HandleAsync(
            new RevokePermissionCommand(id, permission),
            cancellationToken));
    }

    /// <summary>
    /// Lists the action-level permissions granted to a user. Requires user-read rights.
    /// </summary>
    [HttpGet("{id:guid}/permissions")]
    [Authorize(Policy = AuthenticationConfiguration.UsersReadPolicy)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPermissions(Guid id, CancellationToken cancellationToken)
    {
        return Success(await _getUserPermissionsHandler.HandleAsync(
            new GetUserPermissionsQuery(id),
            cancellationToken));
    }

    /// <summary>
    /// Lists the action-level permission catalog. Requires SuperAdmin role.
    /// </summary>
    [HttpGet("permissions")]
    [Authorize(Policy = AuthenticationConfiguration.SuperAdminPolicy)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissionCatalog(CancellationToken cancellationToken)
    {
        return Success(await _getPermissionCatalogHandler.HandleAsync(
            new GetPermissionCatalogQuery(),
            cancellationToken));
    }
}
