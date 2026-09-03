using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly Dictionary<Guid, Product> _products = new();

    public IReadOnlyCollection<Product> Products => _products.Values;

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.TryGetValue(id, out var product) ? product : null);

    public Task<Product?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyCollection<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Product>>(_products.Values
            .Where(product => ids.Contains(product.Id))
            .ToList());

    public Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Product>>(_products.Values.ToList());

    public Task<(IReadOnlyCollection<Product> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = _products.Values
            .Where(product => !product.IsDeleted)
            .OrderBy(product => product.Name)
            .ToList();

        return Task.FromResult((
            (IReadOnlyCollection<Product>)all
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            all.Count));
    }

    public Task<(IReadOnlyCollection<Product> Items, int TotalCount)> SearchAsync(
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
        IEnumerable<Product> query = _products.Values;

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();

            query = query.Where(product =>
                product.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                product.Slug.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (product.Description is not null && product.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (categoryId is not null)
            query = query.Where(product => product.CategoryId == categoryId);

        if (status is not null)
            query = query.Where(product => product.Status == status);

        if (visibility is not null)
            query = query.Where(product => product.Visibility == visibility);

        if (isDeleted is not null)
            query = query.Where(product => product.IsDeleted == isDeleted);

        query = sortBy switch
        {
            CatalogProductSortBy.CreatedAtUtc => sortDirection == CatalogProductSortDirection.Descending
                ? query.OrderByDescending(product => product.CreatedAtUtc)
                : query.OrderBy(product => product.CreatedAtUtc),
            _ => sortDirection == CatalogProductSortDirection.Descending
                ? query.OrderByDescending(product => product.Name)
                : query.OrderBy(product => product.Name)
        };

        var all = query.ToList();

        return Task.FromResult((
            (IReadOnlyCollection<Product>)all
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            all.Count));
    }

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.ContainsKey(id));

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.Values.Any(product => product.Name == name));

    public Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.Values.Any(product => product.Slug == slug));

    public Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        _products[product.Id] = product;

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _products[product.Id] = product;

        return Task.CompletedTask;
    }
}
