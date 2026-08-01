using Xunit;

namespace FluxCommerce.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class IdentityTestCollection : ICollectionFixture<IdentityWebApplicationFactory>
{
    public const string Name = "Identity Integration Tests";
}
