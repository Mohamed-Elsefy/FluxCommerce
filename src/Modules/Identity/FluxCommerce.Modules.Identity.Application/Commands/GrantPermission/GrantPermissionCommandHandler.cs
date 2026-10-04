using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Application.Commands.GrantPermission;

public sealed class GrantPermissionCommandHandler : ICommandHandler<GrantPermissionCommand, Result<UserResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public GrantPermissionCommandHandler(
        IUserRepository userRepository,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public async Task<Result<UserResponse>> HandleAsync(
        GrantPermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new GrantPermissionCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<UserResponse>(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        var accessible = DelegationGuard.CheckTargetAccessible(_currentUser, user);
        if (accessible is not null)
            return Result.Failure<UserResponse>(accessible);

        var allowed = DelegationGuard.CheckGrantAllowed(_currentUser, command.Permission);
        if (allowed is not null)
            return Result.Failure<UserResponse>(allowed);

        var grantResult = user.GrantPermission(command.Permission.Trim());

        if (grantResult.IsFailure)
        {
            if (grantResult.Error == UserErrors.PermissionAlreadyGranted)
                return Result.Failure<UserResponse>(
                    Error.Conflict(UserErrors.PermissionAlreadyGranted.Code, UserErrors.PermissionAlreadyGranted.Description));

            return Result.Failure<UserResponse>(
                Error.Validation(grantResult.Error.Code, grantResult.Error.Description));
        }

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserResponseMapper.ToResponse(user));
    }
}
