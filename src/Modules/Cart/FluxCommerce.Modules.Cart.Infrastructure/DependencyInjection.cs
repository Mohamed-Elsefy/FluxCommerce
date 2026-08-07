using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Cart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCartInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CartOptions>()
            .Bind(configuration.GetSection(CartOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<CartKeyGenerator>();
        services.AddSingleton<CartStateSerializer>();

        return services;
    }
}
