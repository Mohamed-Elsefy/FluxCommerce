using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new ChangePasswordCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var userId = _currentUser.UserId;

        if (userId is null)
            return Result.Failure(
                Error.Unauthorized("Identity.Unauthenticated", "Authentication is required."));

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);

        if (user is null)
            return Result.Failure(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        var currentPasswordIsCorrect = _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash);

        var change = user.ChangePassword(
            currentPasswordIsCorrect,
            _passwordHasher.Hash(command.NewPassword),
            _dateTimeProvider.UtcNow);

        if (change.IsFailure)
            return Result.Failure(
                Error.Validation(UserErrors.InvalidCredentials.Code, "The current password is incorrect."));

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
