using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId) : ICommand<Result<UserResponse>>;
