using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the SQL Server Orders outbox tests behind one shared fixture so parallel test classes
/// do not compete over the same orders database. Individual tests clean up the rows they create.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerOrdersTestCollection : ICollectionFixture<SqlServerOrdersTestFixture>
{
    public const string Name = "SQL Server Orders Outbox Tests";
}