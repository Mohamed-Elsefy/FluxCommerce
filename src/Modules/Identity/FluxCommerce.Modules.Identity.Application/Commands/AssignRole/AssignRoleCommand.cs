using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.AssignRole;

public sealed record AssignRoleCommand(Guid UserId, string Role) : ICommand<Result<UserResponse>>;
