using FluxCommerce.Modules.Catalog.Application.Interfaces;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeCatalogUnitOfWork : ICatalogUnitOfWork
{
    public int SaveChangesCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;

        return Task.FromResult(1);
    }
}
