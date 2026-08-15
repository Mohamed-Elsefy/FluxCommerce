using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 6.11 (G11): the RabbitMQ connection provider must be registered as a container-owned
/// singleton (factory registration, not an instance) so the DI container disposes the provider —
/// and with it the underlying <c>IConnection</c> — when the host shuts down.
/// </summary>
public class RabbitMQServiceExtensionsTests
{
    [Fact]
    public void AddRabbitMQ_RegistersProvider_AsContainerOwnedSingleton()
    {
        var services = new ServiceCollection();

        services.AddRabbitMQ("amqp://localhost:5672/");

        var descriptor = Assert.Single(
            services,
            d => d.ServiceType == typeof(IRabbitMQConnectionProvider));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Null(descriptor.ImplementationInstance);
        Assert.NotNull(descriptor.ImplementationFactory);
    }
}
