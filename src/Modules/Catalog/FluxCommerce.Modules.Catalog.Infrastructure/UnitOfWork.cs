using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;

namespace FluxCommerce.Modules.Catalog.Infrastructure;

public sealed class CatalogUnitOfWork : ICatalogUnitOfWork
{
    private readonly CatalogDbContext _dbContext;

    public CatalogUnitOfWork(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
