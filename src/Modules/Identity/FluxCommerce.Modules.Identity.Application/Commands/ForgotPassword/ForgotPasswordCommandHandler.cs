using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Application.Security;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository,
        ITokenGenerator tokenGenerator,
        IPasswordResetService passwordResetService,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _passwordResetService = passwordResetService;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> HandleAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new ForgotPasswordCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return Result.Success();
        }

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
            return Result.Success();

        var utcNow = _dateTimeProvider.UtcNow;
        var tokenValue = _tokenGenerator.GeneratePasswordResetToken();
        var expiresAtUtc = utcNow.Add(TokenLifetimes.PasswordReset);

        var request = user.RequestPasswordReset(tokenValue, utcNow, expiresAtUtc);

        if (request.IsFailure)
            return Result.Failure(Error.Failure(request.Error.Code, request.Error.Description));

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _passwordResetService.SendPasswordResetEmailAsync(
                user.Id,
                user.Email.Value,
                tokenValue,
                cancellationToken);
        }
        catch (Exception)
        {
            return Result.Success();
        }

        return Result.Success();
    }
}
