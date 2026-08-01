using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Entities;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeTokenRepository : ITokenRepository
{
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<EmailVerificationToken> _verificationTokens = [];
    private readonly List<PasswordResetToken> _resetTokens = [];

    public void Add(RefreshToken token) => _refreshTokens.Add(token);

    public void Add(EmailVerificationToken token) => _verificationTokens.Add(token);

    public void Add(PasswordResetToken token) => _resetTokens.Add(token);

    public Task<RefreshToken?> GetRefreshTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        Task.FromResult(_refreshTokens.FirstOrDefault(token => token.Value == value));

    public Task<EmailVerificationToken?> GetVerificationTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        Task.FromResult(_verificationTokens.FirstOrDefault(token => token.Value == value));

    public Task<PasswordResetToken?> GetPasswordResetTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        Task.FromResult(_resetTokens.FirstOrDefault(token => token.Value == value));
}
