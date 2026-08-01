using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Identity.Infrastructure.Services;

public sealed class PasswordResetService : IPasswordResetService
{
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(ILogger<PasswordResetService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(
        Guid userId,
        string email,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Password reset email queued for user {UserId} ({Email})", userId, email);

        return Task.CompletedTask;
    }
}
