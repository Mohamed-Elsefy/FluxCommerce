using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Commands.AssignRole;
using FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUser;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;
using FluxCommerce.Modules.Identity.Contracts.Requests;
using FluxCommerce.Modules.Identity.Contracts.Responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Identity;

[ApiController]
[Route("admin/users")]
[Authorize(Policy = AuthenticationConfiguration.SuperAdminPolicy)]
[Tags(EndpointTags.Identity)]
public sealed class AdminController : ApiControllerBase
{
    private readonly ICommandHandler<AssignRoleCommand, Result<UserResponse>> _assignRoleHandler;
    private readonly ICommandHandler<UnlockUserCommand, Result<UserResponse>> _unlockUserHandler;
    private readonly IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> _getAdminUsersHandler;
    private readonly IQueryHandler<GetAdminUserQuery, Result<UserAdminResponse>> _getAdminUserHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _getAdminUserCountsHandler;

    public AdminController(
        ICommandHandler<AssignRoleCommand, Result<UserResponse>> assignRoleHandler,
        ICommandHandler<UnlockUserCommand, Result<UserResponse>> unlockUserHandler,
        IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> getAdminUsersHandler,
        IQueryHandler<GetAdminUserQuery, Result<UserAdminResponse>> getAdminUserHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> getAdminUserCountsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _assignRoleHandler = assignRoleHandler;
        _unlockUserHandler = unlockUserHandler;
        _getAdminUsersHandler = getAdminUsersHandler;
        _getAdminUserHandler = getAdminUserHandler;
        _getAdminUserCountsHandler = getAdminUserCountsHandler;
    }

    /// <summary>Assigns a role to a user. Requires SuperAdmin role.</summary>
    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var command = new AssignRoleCommand(id, request.Role);
        return Success(await _assignRoleHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Unlocks a locked user account. Requires SuperAdmin role.</summary>
    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> UnlockUser(Guid id, CancellationToken cancellationToken)
    {
        var command = new UnlockUserCommand(id);
        return Success(await _unlockUserHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>
    /// Lists users, newest first, with optional status, role and text-search filters.
    /// Pagination falls back to page 1 with a page size of 20 (maximum 100).
    /// Requires SuperAdmin role.
    /// </summary>
    [HttpGet]
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
    /// locked out) for the administrative overview. Requires SuperAdmin role.
    /// </summary>
    [HttpGet("counts")]
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
    /// Requires SuperAdmin role.
    /// </summary>
    [HttpGet("{id:guid}")]
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
}
