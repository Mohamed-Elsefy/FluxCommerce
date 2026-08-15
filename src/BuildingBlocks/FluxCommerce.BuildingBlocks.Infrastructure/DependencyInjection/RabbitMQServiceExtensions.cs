using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
}
