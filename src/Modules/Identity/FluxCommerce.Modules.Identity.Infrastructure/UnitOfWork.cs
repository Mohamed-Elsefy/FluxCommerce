using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;

namespace FluxCommerce.Modules.Identity.Infrastructure;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IdentityDbContext _dbContext;

    public UnitOfWork(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
