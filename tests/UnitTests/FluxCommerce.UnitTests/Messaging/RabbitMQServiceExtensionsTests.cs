using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 6.11 (G11): the RabbitMQ connection provider must be registered as a container-owned
/// singleton (factory registration, not an instance) so the DI container disposes the provider —
/// and with it the underlying <c>IConnection</c> — when the host shuts down.
/// Phase 7.5: the consumer infrastructure (registry, message processor, hosted service, options) and
/// the per-handler consumer registration must be wired by the RabbitMQ service extensions.
/// Phase 7.6: the failure classifier (retry/failure policy) must be registered as a singleton and the
/// consumer options must carry the retry policy and dead-letter defaults.
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

    [Fact]
    public async Task AddRabbitMQConsumer_RegistersRegistryProcessorHostedServiceAndOptions()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IRabbitMQConnectionProvider>(new Mock<IRabbitMQConnectionProvider>().Object);
        services.AddRabbitMQConsumer("fluxcommerce.events", "q.orders", options => options.PrefetchCount = 5);

        var registry = Assert.Single(services, d => d.ServiceType == typeof(IntegrationEventConsumerRegistry));
        Assert.Equal(ServiceLifetime.Singleton, registry.Lifetime);

        var contract = Assert.Single(
            services,
            d => d.ServiceType == typeof(IIntegrationEventConsumerRegistry));
        Assert.Equal(ServiceLifetime.Singleton, contract.Lifetime);

        var processor = Assert.Single(
            services,
            d => d.ServiceType == typeof(IIntegrationEventMessageProcessor));
        Assert.Equal(ServiceLifetime.Scoped, processor.Lifetime);
        Assert.Equal(typeof(IntegrationEventMessageProcessor), processor.ImplementationType);

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && d.ImplementationType == typeof(FluxCommerce.BuildingBlocks.Infrastructure.Consuming.RabbitMQConsumerBackgroundService));

        var classifier = Assert.Single(
            services,
            d => d.ServiceType == typeof(IIntegrationEventFailureClassifier));
        Assert.Equal(ServiceLifetime.Singleton, classifier.Lifetime);
        Assert.Equal(typeof(IntegrationEventFailureClassifier), classifier.ImplementationType);

        var deadLetterer = Assert.Single(
            services,
            d => d.ServiceType == typeof(IIntegrationEventDeadLetterer));
        Assert.Equal(ServiceLifetime.Singleton, deadLetterer.Lifetime);
        Assert.NotNull(deadLetterer.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMQConsumerOptions>>().Value;
        Assert.Equal("fluxcommerce.events", options.ExchangeName);
        Assert.Equal("q.orders", options.QueueName);
        Assert.Equal((ushort)5, options.PrefetchCount);
        Assert.Equal(3, options.Retry.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Retry.InitialDelay);
        Assert.True(string.IsNullOrEmpty(options.DeadLetterExchangeName));
        Assert.True(string.IsNullOrEmpty(options.DeadLetterQueueName));
        Assert.Equal("q.orders.dlx", options.ResolvedDeadLetterExchangeName);
        Assert.Equal("q.orders.dlq", options.ResolvedDeadLetterQueueName);
        Assert.NotNull(provider.GetRequiredService<IIntegrationEventFailureClassifier>());
        Assert.NotNull(provider.GetRequiredService<IIntegrationEventDeadLetterer>());

        await provider.DisposeAsync();
    }

    [Fact]
    public void AddIntegrationEventConsumer_RegistersHandlerProcessorAndConsumerDescriptor()
    {
        var services = new ServiceCollection();

        services.AddRabbitMQ("amqp://localhost:5672/");
        services.AddRabbitMQConsumer("fluxcommerce.events", "q.orders");
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, TestOrderCreatedHandler, TestIdempotentProcessor>();

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IntegrationEventConsumerRegistry>();
        var descriptor = registry.Resolve(OrderCreatedIntegrationEvent.EventType);

        Assert.NotNull(descriptor);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, descriptor.EventType);
        Assert.Equal(typeof(OrderCreatedIntegrationEvent), descriptor.EventContractType);
        Assert.Equal(typeof(TestOrderCreatedHandler), descriptor.HandlerType);
        Assert.Equal(typeof(TestIdempotentProcessor), descriptor.ProcessorType);
        Assert.Contains(registry.GetDescriptors(), d => d.EventType == OrderCreatedIntegrationEvent.EventType);

        var handlerDescriptor = Assert.Single(services, d => d.ServiceType == typeof(TestOrderCreatedHandler));
        Assert.Equal(ServiceLifetime.Scoped, handlerDescriptor.Lifetime);

        var processorDescriptor = Assert.Single(services, d => d.ServiceType == typeof(TestIdempotentProcessor));
        Assert.Equal(ServiceLifetime.Scoped, processorDescriptor.Lifetime);
        Assert.NotNull(provider.GetService<TestIdempotentProcessor>());
    }

    public sealed class TestOrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
    {
        public string HandlerName => "UnitTests.TestHandler";

        public Task HandleAsync(OrderCreatedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public sealed class TestIdempotentProcessor : IIdempotentProcessor
    {
        public Task<IdempotencyResult> TryProcessAsync(
            ProcessedMessage message,
            Func<Task> businessOperation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdempotencyResult.NotProcessed());
    }
}
