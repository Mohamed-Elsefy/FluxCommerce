using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<Guid, Category> _categories = new();

    public IReadOnlyCollection<Category> Categories => _categories.Values;

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.TryGetValue(id, out var category) ? category : null);

    public Task<IReadOnlyCollection<Category>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Category>>(_categories.Values.ToList());

    public Task<(IReadOnlyCollection<Category> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = _categories.Values
            .Where(category => !category.IsDeleted)
            .OrderBy(category => category.Name)
            .ToList();

        return Task.FromResult((
            (IReadOnlyCollection<Category>)all
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            all.Count));
    }

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.ContainsKey(id));

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.Values.Any(category => category.Name == name));

    public Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.Values.Any(category => category.Slug == slug));

    public Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        _categories[category.Id] = category;

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        _categories[category.Id] = category;

        return Task.CompletedTask;
    }
}
