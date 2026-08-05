using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Repositories;

public interface IAttributeDefinitionRepository
{
    Task<AttributeDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AttributeDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default);

    Task AddAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default);

    Task UpdateAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default);
}
