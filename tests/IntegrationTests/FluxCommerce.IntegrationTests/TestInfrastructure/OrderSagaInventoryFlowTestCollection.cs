using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the Phase 8.4 Order/Saga command-flow tests behind one shared fixture and keeps them from
/// running in parallel with any other collection (they exercise the real broker and the shared orders
/// and inventory SQL schemas). Each test declares a unique queue on the dedicated test exchange.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrderSagaInventoryFlowTestCollection : ICollectionFixture<OrderSagaInventoryFlowTestFixture>
{
    public const string Name = "Order Saga Inventory Command Flow Tests";
}