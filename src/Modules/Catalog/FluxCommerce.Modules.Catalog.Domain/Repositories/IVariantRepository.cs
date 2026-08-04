using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Repositories;

public interface IVariantRepository
{
    Task<Variant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Variant>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);

    Task AddAsync(Variant variant, CancellationToken cancellationToken = default);

    Task UpdateAsync(Variant variant, CancellationToken cancellationToken = default);
}
