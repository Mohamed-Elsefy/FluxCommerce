using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Commands.AssignRole;
using FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;
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

    public AdminController(
        ICommandHandler<AssignRoleCommand, Result<UserResponse>> assignRoleHandler,
        ICommandHandler<UnlockUserCommand, Result<UserResponse>> unlockUserHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _assignRoleHandler = assignRoleHandler;
        _unlockUserHandler = unlockUserHandler;
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
}
