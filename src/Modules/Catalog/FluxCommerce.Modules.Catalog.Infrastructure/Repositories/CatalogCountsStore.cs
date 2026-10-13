using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Application.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Repositories;

/// <summary>
/// <see cref="ICatalogCountsStore"/> backed by the Catalog DbContext. All aggregates are scalar
/// (<c>CountAsync</c> over boolean/enum predicates) so the queries stay portable across providers.
/// </summary>
public sealed class CatalogCountsStore : ICatalogCountsStore
{
    private readonly CatalogDbContext _dbContext;

    public CatalogCountsStore(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatalogCountsReadModel> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var totalProducts = await _dbContext.Products
            .AsNoTracking()
            .CountAsync(product => !product.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        var activeProducts = await _dbContext.Products
            .AsNoTracking()
            .CountAsync(
                product => !product.IsDeleted && product.Status == ProductStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        var totalCategories = await _dbContext.Categories
            .AsNoTracking()
            .CountAsync(category => !category.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        var totalVariants = await _dbContext.Variants
            .AsNoTracking()
            .CountAsync(variant => !variant.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        return new CatalogCountsReadModel(totalProducts, activeProducts, totalCategories, totalVariants);
    }
}
