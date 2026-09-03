using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Identity.Infrastructure.Services;

public sealed class PasswordResetService : IPasswordResetService
{
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _options;
    private readonly ILocalizer _localizer;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IEmailSender emailSender,
        IOptions<EmailOptions> options,
        ILocalizer localizer,
        ILogger<PasswordResetService> logger)
    {
        _emailSender = emailSender;
        _options = options.Value;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(
        Guid userId,
        string email,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = EmailTemplates.PasswordReset(_localizer, _options, email, resetToken);

            await _emailSender.SendAsync(message, cancellationToken);

            _logger.LogInformation("Password reset email sent to user {UserId} ({Email})", userId, email);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send password reset email to user {UserId} ({Email})", userId, email);

            throw;
        }
    }
}
