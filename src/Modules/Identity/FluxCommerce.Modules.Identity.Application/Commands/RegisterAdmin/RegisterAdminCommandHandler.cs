using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;

public sealed class RegisterAdminCommandHandler : ICommandHandler<RegisterAdminCommand, Result<UserResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public RegisterAdminCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenGenerator tokenGenerator,
        IEmailVerificationService emailVerificationService,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailVerificationService = emailVerificationService;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result<UserResponse>> HandleAsync(
        RegisterAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new RegisterAdminCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.InvalidEmail", "The email address is invalid."));
        }

        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken))
            return Result.Failure<UserResponse>(
                Error.Conflict("Identity.DuplicateEmail", "An account with this email address already exists."));

        var utcNow = _dateTimeProvider.UtcNow;
        var verificationToken = _tokenGenerator.GenerateEmailVerificationToken();
        var verificationTokenExpiresAtUtc = utcNow.Add(TokenLifetimes.EmailVerification);

        var user = User.Register(
            Guid.NewGuid(),
            email,
            _passwordHasher.Hash(command.Password),
            command.FirstName,
            command.LastName,
            utcNow,
            [Roles.Admin],
            verificationToken: verificationToken,
            verificationTokenExpiresAtUtc: verificationTokenExpiresAtUtc);

        // No automatic permissions: a freshly registered Admin starts empty (ADR-016 D5).
        // SuperAdmin grants action-level permissions explicitly afterwards.

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailVerificationService.SendVerificationEmailAsync(
                user.Id,
                user.Email.Value,
                verificationToken,
                cancellationToken);
        }
        catch (Exception)
        {
            return Result.Failure<UserResponse>(
                Error.Failure(
                    "Identity.EmailSendFailed",
                    "The account was created, but the verification email could not be sent. Please request a new verification email."));
        }

        return Result.Success(UserResponseMapper.ToResponse(user));
    }
}
