using Xunit;

namespace FluxCommerce.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class CatalogTestCollection : ICollectionFixture<CatalogWebApplicationFactory>
{
    public const string Name = "Catalog Integration Tests";
}
