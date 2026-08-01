using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;

        return Task.FromResult(1);
    }
}
