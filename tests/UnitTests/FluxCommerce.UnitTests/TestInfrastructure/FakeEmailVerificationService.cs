using FluxCommerce.Modules.Identity.Application.Interfaces.Services;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeEmailVerificationService : IEmailVerificationService
{
    public List<(Guid UserId, string Email, string Token)> Sent { get; } = [];

    public Task SendVerificationEmailAsync(
        Guid userId,
        string email,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        Sent.Add((userId, email, verificationToken));

        return Task.CompletedTask;
    }
}
