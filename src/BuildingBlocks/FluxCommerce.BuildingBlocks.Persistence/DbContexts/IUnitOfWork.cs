namespace FluxCommerce.BuildingBlocks.Persistence.DbContexts;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
