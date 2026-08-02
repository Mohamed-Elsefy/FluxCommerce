using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Application.Commands.Refresh;

public sealed class RefreshCommandHandler : ICommandHandler<RefreshCommand, Result<LoginResponse>>
{
    private readonly ITokenRepository _tokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtProvider _jwtProvider;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshCommandHandler(
        ITokenRepository tokenRepository,
        IUserRepository userRepository,
        IJwtProvider jwtProvider,
        ITokenGenerator tokenGenerator,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _tokenRepository = tokenRepository;
        _userRepository = userRepository;
        _jwtProvider = jwtProvider;
        _tokenGenerator = tokenGenerator;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResponse>> HandleAsync(
        RefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new RefreshCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure<LoginResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        var token = await _tokenRepository.GetRefreshTokenByValueAsync(command.RefreshToken, cancellationToken);

        if (token is null)
            return Result.Failure<LoginResponse>(InvalidToken());

        var user = await _userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<LoginResponse>(InvalidToken());

        var utcNow = _dateTimeProvider.UtcNow;

        var rotation = user.RotateRefreshToken(
            token.Value,
            _tokenGenerator.GenerateRefreshToken(),
            utcNow,
            utcNow.Add(TokenLifetimes.RefreshToken));

        if (rotation.IsFailure)
            return Result.Failure<LoginResponse>(InvalidToken());

        var accessToken = _jwtProvider.CreateAccessToken(user.Id, user.Email.Value, user.Roles);

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(LoginResponseMapper.ToResponse(user, accessToken, rotation.Value));
    }

    private static Error InvalidToken() =>
        Error.Unauthorized(UserErrors.InvalidToken.Code, UserErrors.InvalidToken.Description);
}
