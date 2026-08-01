using FluxCommerce.Modules.Identity.Domain.Entities;

namespace FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;

public interface ITokenRepository
{
    Task<RefreshToken?> GetRefreshTokenByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<EmailVerificationToken?> GetVerificationTokenByValueAsync(string value, CancellationToken cancellationToken = default);

    Task<PasswordResetToken?> GetPasswordResetTokenByValueAsync(string value, CancellationToken cancellationToken = default);
}
