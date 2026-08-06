using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeVariantRepository : IVariantRepository
{
    private readonly Dictionary<Guid, Variant> _variants = new();

    public IReadOnlyCollection<Variant> Variants => _variants.Values;

    public Task<Variant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variants.TryGetValue(id, out var variant) ? variant : null);

    public Task<IReadOnlyCollection<Variant>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Variant>>(_variants.Values.ToList());

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variants.ContainsKey(id));

    public Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variants.Values.Any(variant => variant.Sku == sku));

    public Task<IReadOnlyCollection<Variant>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Variant>>(_variants.Values.Where(variant => variant.ProductId == productId).ToList());

    public Task AddAsync(Variant variant, CancellationToken cancellationToken = default)
    {
        _variants[variant.Id] = variant;

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Variant variant, CancellationToken cancellationToken = default)
    {
        _variants[variant.Id] = variant;

        return Task.CompletedTask;
    }
}
