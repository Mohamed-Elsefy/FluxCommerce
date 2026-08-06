using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Repositories;

public sealed class VariantRepository : IVariantRepository
{
    private readonly CatalogDbContext _dbContext;

    public VariantRepository(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private DbSet<Variant> Variants => _dbContext.Set<Variant>();

    public Task<Variant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Variants.FirstOrDefaultAsync(variant => variant.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<Variant>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Variants
            .Where(variant => !variant.IsDeleted)
            .OrderBy(variant => variant.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Variants.AnyAsync(variant => variant.Id == id, cancellationToken);

    public Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        Variants.AnyAsync(variant => variant.Sku.Value == sku.Value, cancellationToken);

    public async Task<IReadOnlyCollection<Variant>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var variants = await Variants.AsNoTracking()
            .Where(variant => variant.ProductId == productId && !variant.IsDeleted)
            .ToListAsync(cancellationToken);

        return variants;
    }

    public Task AddAsync(Variant variant, CancellationToken cancellationToken = default)
    {
        Variants.Add(variant);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Variant variant, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
