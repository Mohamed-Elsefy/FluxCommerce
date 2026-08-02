using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand, Result>
{
    private readonly ITokenRepository _tokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public ResetPasswordCommandHandler(
        ITokenRepository tokenRepository,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _tokenRepository = tokenRepository;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new ResetPasswordCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var token = await _tokenRepository.GetPasswordResetTokenByValueAsync(command.Token, cancellationToken);

        if (token is null)
            return Result.Failure(InvalidToken());

        var user = await _userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null)
            return Result.Failure(InvalidToken());

        var reset = user.ResetPassword(
            token.Value,
            _passwordHasher.Hash(command.NewPassword),
            _dateTimeProvider.UtcNow);

        if (reset.IsFailure)
            return Result.Failure(InvalidToken());

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error InvalidToken() =>
        Error.Validation(UserErrors.InvalidToken.Code, UserErrors.InvalidToken.Description);
}
