using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Errors;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Commands.Login;

public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtProvider _jwtProvider;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtProvider jwtProvider,
        ITokenGenerator tokenGenerator,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtProvider = jwtProvider;
        _tokenGenerator = tokenGenerator;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResponse>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new LoginCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure<LoginResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        var utcNow = _dateTimeProvider.UtcNow;

        if (user is null)
        {
            _passwordHasher.Verify(command.Password, DummyPasswordHash);
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        var credentialsMatch = _passwordHasher.Verify(command.Password, user.PasswordHash);
        var authentication = user.Authenticate(credentialsMatch, utcNow);

        if (authentication.IsFailure)
        {
            if (authentication.Error == UserErrors.InvalidCredentials)
            {
                await _userRepository.UpdateAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Failure<LoginResponse>(MapFailure(authentication.Error));
        }

        var accessToken = _jwtProvider.CreateAccessToken(user.Id, user.Email.Value, user.Roles);

        var refreshToken = user.IssueRefreshToken(
            _tokenGenerator.GenerateRefreshToken(),
            utcNow,
            utcNow.Add(TokenLifetimes.RefreshToken));

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(LoginResponseMapper.ToResponse(user, accessToken, refreshToken));
    }

    private static Error InvalidCredentials() =>
        Error.Unauthorized(UserErrors.InvalidCredentials.Code, UserErrors.InvalidCredentials.Description);

    private const string DummyPasswordHash =
        "PBKDF2$100000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static Error MapFailure(DomainError error)
    {
        if (error == UserErrors.InvalidCredentials)
            return Error.Unauthorized(error.Code, error.Description);

        if (error == UserErrors.EmailNotVerified || error == UserErrors.AccountLocked)
            return Error.Forbidden(error.Code, error.Description);

        return Error.Failure(error.Code, error.Description);
    }
}
