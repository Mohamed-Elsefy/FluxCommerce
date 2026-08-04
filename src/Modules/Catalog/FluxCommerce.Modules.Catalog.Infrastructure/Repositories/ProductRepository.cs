using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductRepository(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private DbSet<Product> Products => _dbContext.Set<Product>();

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Products.AnyAsync(product => product.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Products.AnyAsync(product => product.Name == name, cancellationToken);

    public Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default) =>
        Products.AnyAsync(product => product.Slug.Value == slug.Value, cancellationToken);

    public Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        Products.Add(product);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
