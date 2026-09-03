using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailCommandHandler : ICommandHandler<VerifyEmailCommand, Result<UserResponse>>
{
    private readonly ITokenRepository _tokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public VerifyEmailCommandHandler(
        ITokenRepository tokenRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _tokenRepository = tokenRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result<UserResponse>> HandleAsync(
        VerifyEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new VerifyEmailCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var token = await _tokenRepository.GetVerificationTokenByValueAsync(command.Token, cancellationToken);

        if (token is null)
            return Result.Failure<UserResponse>(InvalidToken());

        var user = await _userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<UserResponse>(InvalidToken());

        var verification = user.VerifyEmail(token.Value, _dateTimeProvider.UtcNow);

        if (verification.IsFailure)
            return Result.Failure<UserResponse>(InvalidToken());

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserResponseMapper.ToResponse(user));
    }

    private static Error InvalidToken() =>
        Error.Validation(UserErrors.InvalidToken.Code, UserErrors.InvalidToken.Description);
}
