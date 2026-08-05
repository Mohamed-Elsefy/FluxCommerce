using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Repositories;

public sealed class AttributeDefinitionRepository : IAttributeDefinitionRepository
{
    private readonly CatalogDbContext _dbContext;

    public AttributeDefinitionRepository(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private DbSet<AttributeDefinition> AttributeDefinitions => _dbContext.Set<AttributeDefinition>();

    public Task<AttributeDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        AttributeDefinitions.FirstOrDefaultAsync(attributeDefinition => attributeDefinition.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<AttributeDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await AttributeDefinitions
            .Where(attributeDefinition => !attributeDefinition.IsDeleted)
            .OrderBy(attributeDefinition => attributeDefinition.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        AttributeDefinitions.AnyAsync(attributeDefinition => attributeDefinition.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        AttributeDefinitions.AnyAsync(attributeDefinition => attributeDefinition.Name == name, cancellationToken);

    public Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default) =>
        AttributeDefinitions.AnyAsync(attributeDefinition => attributeDefinition.Slug.Value == slug.Value, cancellationToken);

    public Task AddAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default)
    {
        AttributeDefinitions.Add(attributeDefinition);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
