using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the Phase 8.4 reservation-request consumer tests behind one shared fixture and keeps
/// them from running in parallel with any other collection (they exercise the real broker and the
/// shared inventory SQL schema). Each test declares a unique queue on the dedicated test exchange.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RabbitMQInventoryConsumerTestCollection : ICollectionFixture<RabbitMQInventoryConsumerTestFixture>
{
    public const string Name = "RabbitMQ Inventory Consumer Tests";
}