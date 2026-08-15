using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Serializes the RabbitMQ publisher tests behind one shared fixture. Tests run against the real
/// broker on the dedicated <see cref="RabbitMQTestFixture.TestExchangeName"/> exchange; each test
/// uses a unique exclusive queue so parallel runs cannot interfere.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RabbitMQTestCollection : ICollectionFixture<RabbitMQTestFixture>
{
    public const string Name = "RabbitMQ Publisher Tests";
}