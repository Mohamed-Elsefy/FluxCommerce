using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the SQL Server Orders outbox tests behind one shared fixture so parallel test classes
/// do not compete over the same orders database. Individual tests clean up the rows they create.
/// Parallelization is disabled so this collection never overlaps the other collections whose full
/// application hosts run the Orders outbox dispatcher against this same database.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerOrdersTestCollection : ICollectionFixture<SqlServerOrdersTestFixture>
{
    public const string Name = "SQL Server Orders Outbox Tests";
}