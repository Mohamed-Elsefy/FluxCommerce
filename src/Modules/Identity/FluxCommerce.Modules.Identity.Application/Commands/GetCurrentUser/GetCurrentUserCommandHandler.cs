using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.GetCurrentUser;

public sealed class GetCurrentUserCommandHandler : ICommandHandler<GetCurrentUserCommand, Result<UserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;

    public GetCurrentUserCommandHandler(ICurrentUser currentUser, IUserRepository userRepository)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
    }

    public async Task<Result<UserResponse>> HandleAsync(
        GetCurrentUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Result.Failure<UserResponse>(
                Error.Unauthorized("Identity.Unauthenticated", "Authentication is required."));

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);

        if (user is null)
            return Result.Failure<UserResponse>(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        return Result.Success(UserResponseMapper.ToResponse(user));
    }
}
