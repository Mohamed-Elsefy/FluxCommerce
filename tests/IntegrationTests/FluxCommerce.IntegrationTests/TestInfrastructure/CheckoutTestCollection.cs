using Xunit;

namespace FluxCommerce.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class CheckoutTestCollection : ICollectionFixture<CheckoutWebApplicationFactory>
{
    public const string Name = "Checkout Integration Tests";
}