namespace FluxCommerce.Modules.Identity.Application.Interfaces.Services;

public interface IEmailVerificationService
{
    Task SendVerificationEmailAsync(
        Guid userId,
        string email,
        string verificationToken,
        CancellationToken cancellationToken = default);
}
