using FluxCommerce.Modules.Identity.Application.Interfaces.Services;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakePasswordResetService : IPasswordResetService
{
    public List<(Guid UserId, string Email, string Token)> Sent { get; } = [];

    public Task SendPasswordResetEmailAsync(
        Guid userId,
        string email,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        Sent.Add((userId, email, resetToken));

        return Task.CompletedTask;
    }
}
