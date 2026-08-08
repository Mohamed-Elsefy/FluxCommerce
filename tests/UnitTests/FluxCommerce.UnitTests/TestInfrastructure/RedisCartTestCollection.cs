using Xunit;

namespace FluxCommerce.UnitTests.TestInfrastructure;

[CollectionDefinition(Name)]
public sealed class RedisCartTestCollection : ICollectionFixture<RedisCartFixture>
{
    public const string Name = "Redis Cart Integration Tests";
}
