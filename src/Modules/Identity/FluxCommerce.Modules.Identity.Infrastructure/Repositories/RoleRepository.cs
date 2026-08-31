using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Identity.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IdentityDbContext _dbContext;

    public RoleRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyCollection<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var names = await _dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.NormalizedName)
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        return names;
    }

    public Task<bool> ExistsAsync(string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            return Task.FromResult(false);

        var normalized = role.Trim().ToUpperInvariant();

        return _dbContext.Roles
            .AsNoTracking()
            .AnyAsync(r => r.NormalizedName == normalized, cancellationToken);
    }

    public async Task AddAsync(string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A role name is required.", nameof(role));

        var trimmed = role.Trim();

        if (trimmed.Length > Role.NameMaxLength)
            throw new ArgumentException($"Role name must not exceed {Role.NameMaxLength} characters.", nameof(role));

        var normalized = trimmed.ToUpperInvariant();

        var exists = await _dbContext.Roles
            .AnyAsync(r => r.NormalizedName == normalized, cancellationToken);

        if (exists)
            return;

        var entity = Role.Create(trimmed, DateTimeOffset.UtcNow);

        _dbContext.Roles.Add(entity);
    }
}
