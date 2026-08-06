using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeAttributeDefinitionRepository : IAttributeDefinitionRepository
{
    private readonly Dictionary<Guid, AttributeDefinition> _attributeDefinitions = new();

    public IReadOnlyCollection<AttributeDefinition> AttributeDefinitions => _attributeDefinitions.Values;

    public Task<AttributeDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attributeDefinitions.TryGetValue(id, out var attributeDefinition) ? attributeDefinition : null);

    public Task<IReadOnlyCollection<AttributeDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<AttributeDefinition>>(_attributeDefinitions.Values.ToList());

    public Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attributeDefinitions.ContainsKey(id));

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attributeDefinitions.Values.Any(attributeDefinition => attributeDefinition.Name == name));

    public Task<bool> ExistsBySlugAsync(Slug slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attributeDefinitions.Values.Any(attributeDefinition => attributeDefinition.Slug == slug));

    public Task AddAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default)
    {
        _attributeDefinitions[attributeDefinition.Id] = attributeDefinition;

        return Task.CompletedTask;
    }

    public Task UpdateAsync(AttributeDefinition attributeDefinition, CancellationToken cancellationToken = default)
    {
        _attributeDefinitions[attributeDefinition.Id] = attributeDefinition;

        return Task.CompletedTask;
    }
}
