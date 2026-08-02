using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FluxCommerce.Modules.Identity.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _dbContext;

    public UserRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private DbSet<User> Users => _dbContext.Set<User>();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Users.FirstOrDefaultAsync(user => user.Email.Value == email.Value, cancellationToken);

    public Task<bool> ExistsByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Users.AnyAsync(user => user.Email.Value == email.Value, cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        Users.Add(user);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.Entry(user);

        MarkNewOwnedAsAdded(entry.Reference(u => u.VerificationToken).TargetEntry);
        MarkNewOwnedAsAdded(entry.Reference(u => u.ResetToken).TargetEntry);

        foreach (var refreshToken in user.RefreshTokens)
        {
            MarkNewOwnedAsAdded(_dbContext.Entry(refreshToken));
        }

        return Task.CompletedTask;
    }

    private static void MarkNewOwnedAsAdded(EntityEntry? targetEntry)
    {
        if (targetEntry is not { State: EntityState.Modified })
            return;

        var originalValues = targetEntry.OriginalValues;
        var currentValues = targetEntry.CurrentValues;

        if (originalValues.Properties.All(property => Equals(originalValues[property.Name], currentValues[property.Name])))
        {
            targetEntry.State = EntityState.Added;
        }
    }
}
