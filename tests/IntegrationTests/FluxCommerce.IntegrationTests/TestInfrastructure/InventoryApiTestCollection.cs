using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the Phase 5.8 Inventory API tests behind one shared in-memory host.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InventoryApiTestCollection : ICollectionFixture<InventoryWebApplicationFactory>
{
    public const string Name = "Inventory API Tests";
}
