using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the Orders outbox dispatcher tests behind one shared fixture. Parallelization is
/// disabled so this collection never overlaps the other collections that share the orders database
/// (and never races the publisher tests over the shared test exchange).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerOrdersDispatcherTestCollection : ICollectionFixture<OrdersOutboxDispatcherTestFixture>
{
    public const string Name = "SQL Server Orders Outbox Dispatcher Tests";
}