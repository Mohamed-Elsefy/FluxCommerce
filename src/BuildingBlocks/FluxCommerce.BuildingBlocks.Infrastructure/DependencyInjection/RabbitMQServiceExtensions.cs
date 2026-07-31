using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;

public static class RabbitMQServiceExtensions
{
    public static IServiceCollection AddRabbitMQ(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IRabbitMQConnectionProvider>(new RabbitMQConnectionProvider(connectionString));

        return services;
    }
}
