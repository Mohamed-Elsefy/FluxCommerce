using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
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
        Products
            .Include(product => product.AttributeValues)
            .Include(product => product.Specifications)
            .Include(product => product.Images)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<Product?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Products.AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Products.AsNoTracking()
            .Where(product => !product.IsDeleted)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyCollection<Product> Items, int TotalCount)> SearchAsync(
        string searchTerm,
        Guid? categoryId = null,
        ProductStatus? status = null,
        ProductVisibility? visibility = null,
        bool? isDeleted = null,
        CatalogProductSortBy? sortBy = null,
        CatalogProductSortDirection? sortDirection = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Product> query = Products.AsNoTracking();

        query = isDeleted == true
            ? query.Where(product => product.IsDeleted)
            : query.Where(product => !product.IsDeleted);

        query = query.Include(product => product.Images.Where(image => image.IsPrimary));

        if (categoryId is not null)
            query = query.Where(product => product.CategoryId == categoryId.Value);

        if (status is not null)
            query = query.Where(product => product.Status == status.Value);

        if (visibility is not null)
            query = query.Where(product => product.Visibility == visibility.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();

            var variants = _dbContext.Set<Variant>();

            query = query.Where(product =>
                product.Name.Contains(term) ||
                product.Slug.Value.Contains(term) ||
                (product.Description != null && product.Description.Contains(term)) ||
                variants.Any(variant =>
                    !variant.IsDeleted &&
                    variant.ProductId == product.Id &&
                    variant.Sku.Value.Contains(term)));
        }

        query = ApplyOrdering(query, sortBy, sortDirection);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    private static IQueryable<Product> ApplyOrdering(
        IQueryable<Product> query,
        CatalogProductSortBy? sortBy,
        CatalogProductSortDirection? sortDirection)
    {
        var descending = sortDirection == CatalogProductSortDirection.Descending;

        return (sortBy, descending) switch
        {
            (CatalogProductSortBy.CreatedAtUtc, false) => query.OrderBy(product => product.CreatedAtUtc),
            (CatalogProductSortBy.CreatedAtUtc, true) => query.OrderByDescending(product => product.CreatedAtUtc),
            (CatalogProductSortBy.Name, true) => query.OrderByDescending(product => product.Name),
            _ => query.OrderBy(product => product.Name)
        };
    }

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
