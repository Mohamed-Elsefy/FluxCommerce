using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes all SQL Server concurrency tests behind one shared fixture so parallel test classes do
/// not compete over the same inventory database. Individual tests clean up the rows they create.
/// Parallelization is disabled so this collection never overlaps the Inventory API collection, whose
/// full application host runs the reservation-expiration background service against the same database
/// and could otherwise write concurrent outbox records (or expire reservations) mid-test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerInventoryTestCollection : ICollectionFixture<SqlServerInventoryTestFixture>
{
    public const string Name = "SQL Server Inventory Concurrency Tests";
}
