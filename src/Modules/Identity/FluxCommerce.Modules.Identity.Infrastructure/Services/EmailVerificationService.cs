using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Identity.Infrastructure.Services;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(ILogger<EmailVerificationService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(
        Guid userId,
        string email,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Verification email queued for user {UserId} ({Email})", userId, email);

        return Task.CompletedTask;
    }
}
