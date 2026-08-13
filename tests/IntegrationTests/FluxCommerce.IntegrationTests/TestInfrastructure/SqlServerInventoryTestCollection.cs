using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes all SQL Server concurrency tests behind one shared fixture so parallel test classes do
/// not compete over the same inventory database. Individual tests clean up the rows they create.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerInventoryTestCollection : ICollectionFixture<SqlServerInventoryTestFixture>
{
    public const string Name = "SQL Server Inventory Concurrency Tests";
}
