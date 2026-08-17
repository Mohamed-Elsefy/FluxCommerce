using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the Phase 7.9 real-consumer tests behind one shared fixture and keeps them from running
/// in parallel with any other collection (they exercise the real broker and the shared orders SQL
/// schema). Each test declares a unique queue on the dedicated test exchange.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RabbitMQOrdersConsumerTestCollection : ICollectionFixture<RabbitMQOrdersConsumerTestFixture>
{
    public const string Name = "RabbitMQ Orders Consumer Tests";
}