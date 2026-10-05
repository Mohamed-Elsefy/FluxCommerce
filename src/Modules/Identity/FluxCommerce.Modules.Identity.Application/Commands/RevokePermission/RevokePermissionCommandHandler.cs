using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.RevokePermission;

public sealed class RevokePermissionCommandHandler : ICommandHandler<RevokePermissionCommand, Result<UserResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public RevokePermissionCommandHandler(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public async Task<Result<UserResponse>> HandleAsync(
        RevokePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RevokePermissionCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<UserResponse>(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        user.RevokePermission(command.Permission.Trim());

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserResponseMapper.ToResponse(user));
    }
}
