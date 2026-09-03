using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Entities;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Identity.Infrastructure.Repositories;

public sealed class TokenRepository : ITokenRepository
{
    private readonly IdentityDbContext _dbContext;

    public TokenRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RefreshToken?> GetRefreshTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        _dbContext.Set<User>()
            .AsNoTracking()
            .SelectMany(user => user.RefreshTokens)
            .FirstOrDefaultAsync(token => token.Value == value, cancellationToken);

    public Task<EmailVerificationToken?> GetVerificationTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        _dbContext.Set<User>()
            .AsNoTracking()
            .Select(user => user.VerificationToken)
            .Where(token => token != null && token.Value == value)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PasswordResetToken?> GetPasswordResetTokenByValueAsync(string value, CancellationToken cancellationToken = default) =>
        _dbContext.Set<User>()
            .AsNoTracking()
            .Select(user => user.ResetToken)
            .Where(token => token != null && token.Value == value)
            .FirstOrDefaultAsync(cancellationToken);
}
