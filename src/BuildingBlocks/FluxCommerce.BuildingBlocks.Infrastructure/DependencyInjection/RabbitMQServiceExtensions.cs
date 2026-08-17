using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;

public static class RabbitMQServiceExtensions
{
    public static IServiceCollection AddRabbitMQ(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IRabbitMQConnectionProvider>(_ => new RabbitMQConnectionProvider(connectionString));

        return services;
    }

    public static IServiceCollection AddRabbitMQPublisher(this IServiceCollection services, string exchangeName)
    {
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("A RabbitMQ exchange name must be provided.", nameof(exchangeName));

        services.AddSingleton<IRabbitMQPublisher>(serviceProvider =>
            new RabbitMQPublisher(
                serviceProvider.GetRequiredService<IRabbitMQConnectionProvider>(),
                serviceProvider.GetRequiredService<ILogger<RabbitMQPublisher>>(),
                exchangeName));

        return services;
    }

    /// <summary>
    /// Registers the Phase 7.5/7.6/7.7 consumer infrastructure: the singleton consumer registry
    /// (enumerating every declared <c>AddIntegrationEventConsumer&lt;,&gt;</c> descriptor for queue
    /// binding), the scoped message processor, the failure classifier (retry/failure policy), the
    /// dead-letter publisher (terminal-failure enrichment to the dead-letter queue), the
    /// <see cref="RabbitMQConsumerOptions"/> (binding the consumer to the Phase 6 topic exchange and the
    /// given queue, including the Phase 7.6/7.7 retry policy and dead-letter topology), and the consumer
    /// hosted service. The host consumes from the SAME Phase 6 topology — exchange + routing keys —
    /// never a parallel messaging architecture. Requires the module's <c>IIdempotentProcessor</c> to be
    /// registered so every delivery can be processed through the atomic idempotency boundary.
    /// </summary>
    public static IServiceCollection AddRabbitMQConsumer(
        this IServiceCollection services,
        string exchangeName,
        string queueName,
        Action<RabbitMQConsumerOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("A RabbitMQ exchange name must be provided.", nameof(exchangeName));

        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("A RabbitMQ consumer queue name must be provided.", nameof(queueName));

        services.AddSingleton<IntegrationEventConsumerRegistry>();
        services.AddSingleton<IIntegrationEventConsumerRegistry>(serviceProvider =>
            serviceProvider.GetRequiredService<IntegrationEventConsumerRegistry>());
        services.AddSingleton<IIntegrationEventFailureClassifier, IntegrationEventFailureClassifier>();
        services.AddSingleton<IIntegrationEventDeadLetterer>(serviceProvider =>
            new IntegrationEventDeadLetterer(
                serviceProvider.GetRequiredService<IRabbitMQConnectionProvider>(),
                serviceProvider.GetRequiredService<ILogger<IntegrationEventDeadLetterer>>(),
                serviceProvider.GetRequiredService<IOptions<RabbitMQConsumerOptions>>().Value.ResolvedDeadLetterExchangeName));
        services.AddScoped<IIntegrationEventMessageProcessor, IntegrationEventMessageProcessor>();

        services.AddOptions<RabbitMQConsumerOptions>().Configure(options =>
        {
            options.ExchangeName = exchangeName;
            options.QueueName = queueName;
            configure?.Invoke(options);
        });

        services.AddHostedService<RabbitMQConsumerBackgroundService>();

        return services;
    }

    /// <summary>
    /// Declares that the <see cref="IIntegrationEventHandler{TEvent}"/> implementation
    /// <typeparamref name="THandler"/> consumes <typeparamref name="TEvent"/> from the message broker.
    /// The handler is registered in the container (scoped, so it shares the module DbContext and
    /// <c>IIdempotentProcessor</c> of the per-delivery scope), and a consumer descriptor is contributed
    /// to the consumer registry so the consumer host binds its queue to the event's routing key and the
    /// message processor resolves and invokes the handler.
    /// </summary>
    public static IServiceCollection AddIntegrationEventConsumer<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        var eventType = IntegrationEventTypeResolver.Get(typeof(TEvent));

        services.AddScoped<THandler>();
        services.AddSingleton(new IntegrationEventConsumerDescriptorRegistration(
            new IntegrationEventConsumerDescriptor
            {
                EventType = eventType,
                EventContractType = typeof(TEvent),
                HandlerType = typeof(THandler)
            }));

        return services;
    }
}
