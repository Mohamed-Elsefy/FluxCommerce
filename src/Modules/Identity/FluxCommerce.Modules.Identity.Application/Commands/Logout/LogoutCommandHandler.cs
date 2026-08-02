using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;

namespace FluxCommerce.Modules.Identity.Application.Commands.Logout;

public sealed class LogoutCommandHandler : ICommandHandler<LogoutCommand, Result>
{
    private readonly ITokenRepository _tokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(
        ITokenRepository tokenRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _tokenRepository = tokenRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> HandleAsync(
        LogoutCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new LogoutCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var token = await _tokenRepository.GetRefreshTokenByValueAsync(command.RefreshToken, cancellationToken);

        if (token is null)
            return Result.Success();

        var user = await _userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null)
            return Result.Success();

        var logout = user.Logout(token.Value, _dateTimeProvider.UtcNow);

        if (logout.IsFailure)
            return Result.Failure(Error.Unauthorized(logout.Error.Code, logout.Error.Description));

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
