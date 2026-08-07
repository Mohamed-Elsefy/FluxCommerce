using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Infrastructure;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CartInfrastructureRegistrationTests
{
    [Fact]
    public void AddCartInfrastructure_RegistersCartOptions_FromConfiguration()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CartOptions>>().Value;

        Assert.Equal("cart", options.KeyNamespace);
        Assert.Equal(10080, options.DefaultTtlMinutes);
    }

    [Fact]
    public void AddCartInfrastructure_WithInvalidOptions_ThrowsValidationException()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart items", "10080"));

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CartOptions>>().Value);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersICartStore_AsRedisCartStore_Singleton()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICartStore))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(RedisCartStore), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_WithRedis_ResolvesRedisCartStore()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));
        services.AddRedis("localhost:6379");

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisCartStore>(provider.GetRequiredService<ICartStore>());
    }

    [Fact]
    public void AddCartInfrastructure_DoesNotRegister_RedisConnectionProvider()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRedisConnectionProvider))
            .ToList();

        Assert.Empty(registrations);
    }

    [Fact]
    public void AddRedis_RegistersRedisConnectionProvider_Once_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRedis("localhost:6379");

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRedisConnectionProvider))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(ServiceLifetime.Singleton, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersCartKeyGenerator()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CartKeyGenerator>());
    }

    [Fact]
    public void AddCartInfrastructure_RegistersCartStateSerializer()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", "10080"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CartStateSerializer>());
    }

    private static IConfiguration CreateConfiguration(string keyNamespace, string ttlMinutes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CartOptions.SectionName}:KeyNamespace"] = keyNamespace,
                [$"{CartOptions.SectionName}:DefaultTtlMinutes"] = ttlMinutes,
            })
            .Build();
}
