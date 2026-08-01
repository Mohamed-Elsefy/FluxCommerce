namespace FluxCommerce.Modules.Identity.Application.Interfaces.Services;

public interface IPasswordResetService
{
    Task SendPasswordResetEmailAsync(
        Guid userId,
        string email,
        string resetToken,
        CancellationToken cancellationToken = default);
}
