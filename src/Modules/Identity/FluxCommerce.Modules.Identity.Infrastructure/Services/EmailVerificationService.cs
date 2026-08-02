using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Identity.Infrastructure.Services;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        IEmailSender emailSender,
        IOptions<EmailOptions> options,
        ILogger<EmailVerificationService> logger)
    {
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(
        Guid userId,
        string email,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = EmailTemplates.Verification(_options, email, verificationToken);

            await _emailSender.SendAsync(message, cancellationToken);

            _logger.LogInformation("Verification email sent to user {UserId} ({Email})", userId, email);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send verification email to user {UserId} ({Email})", userId, email);

            throw;
        }
    }
}
