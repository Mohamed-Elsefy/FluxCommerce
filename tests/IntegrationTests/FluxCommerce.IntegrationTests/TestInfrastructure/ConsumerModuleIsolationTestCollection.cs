using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7 P0 regression collection: composes BOTH module fixtures (Orders + Inventory) so the consumer
/// module-isolation tests run against a production-like container graph (both modules' infrastructure
/// registered) with both real SQL Server databases and the real broker. Serialized like the other
/// consumer collections because they exercise the real broker and the shared schemas.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsumerModuleIsolationTestCollection :
    ICollectionFixture<RabbitMQOrdersConsumerTestFixture>,
    ICollectionFixture<RabbitMQConsumerTestFixture>
{
    public const string Name = "Consumer Module Isolation Tests";
}
