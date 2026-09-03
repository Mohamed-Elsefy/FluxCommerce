using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;

public sealed class ResendVerificationCommandHandler : ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public ResendVerificationCommandHandler(
        IUserRepository userRepository,
        ITokenGenerator tokenGenerator,
        IEmailVerificationService emailVerificationService,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _emailVerificationService = emailVerificationService;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result<ResendVerificationResponse>> HandleAsync(
        ResendVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new ResendVerificationCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<ResendVerificationResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return Result.Success(new ResendVerificationResponse(false, null));
        }

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null || user.IsEmailVerified)
            return Result.Success(new ResendVerificationResponse(false, null));

        var utcNow = _dateTimeProvider.UtcNow;
        var tokenValue = _tokenGenerator.GenerateEmailVerificationToken();
        var expiresAtUtc = utcNow.Add(TokenLifetimes.EmailVerification);

        var resend = user.ResendVerification(tokenValue, utcNow, expiresAtUtc);

        if (resend.IsFailure)
            return Result.Failure<ResendVerificationResponse>(
                Error.Failure(resend.Error.Code, resend.Error.Description));

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailVerificationService.SendVerificationEmailAsync(
                user.Id,
                user.Email.Value,
                tokenValue,
                cancellationToken);
        }
        catch (Exception)
        {
            return Result.Success(new ResendVerificationResponse(false, null));
        }

        return Result.Success(new ResendVerificationResponse(true, expiresAtUtc));
    }
}
