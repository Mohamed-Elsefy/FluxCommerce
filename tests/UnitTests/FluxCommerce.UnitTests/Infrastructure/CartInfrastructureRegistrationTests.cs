using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
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

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CartOptions>>().Value;

        Assert.Equal("cart", options.KeyNamespace);
    }

    [Fact]
    public void AddCartInfrastructure_WithInvalidOptions_ThrowsValidationException()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart items"));

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CartOptions>>().Value);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersICartStore_AsRedisCartStore_Singleton()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

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

        services.AddCartInfrastructure(CreateConfiguration("cart"));
        services.AddRedis("localhost:6379");

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisCartStore>(provider.GetRequiredService<ICartStore>());
    }

    [Fact]
    public void AddCartInfrastructure_DoesNotRegister_RedisConnectionProvider()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

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

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CartKeyGenerator>());
    }

    [Fact]
    public void AddCartInfrastructure_RegistersCartStateSerializer()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CartStateSerializer>());
    }

    [Fact]
    public void AddCartInfrastructure_RegistersCreateGuestCartCommandHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<CreateGuestCartCommand, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(CreateGuestCartCommandHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersGetGuestCartQueryHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IQueryHandler<GetGuestCartQuery, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(GetGuestCartQueryHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersAddGuestCartItemCommandHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<AddGuestCartItemCommand, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(AddGuestCartItemCommandHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersUpdateGuestCartItemQuantityCommandHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<UpdateGuestCartItemQuantityCommand, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(UpdateGuestCartItemQuantityCommandHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersRemoveGuestCartItemCommandHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<RemoveGuestCartItemCommand, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(RemoveGuestCartItemCommandHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_RegistersClearGuestCartCommandHandler()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<ClearGuestCartCommand, Result<CartState>>))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(ClearGuestCartCommandHandler), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCartInfrastructure_BindsDefaultTtlMinutes_FromConfiguration()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart", defaultTtlMinutes: 45));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CartOptions>>().Value;

        Assert.Equal(45, options.DefaultTtlMinutes);
    }

    [Fact]
    public void AddCartInfrastructure_WithoutTtlConfiguration_UsesDefault()
    {
        var services = new ServiceCollection();

        services.AddCartInfrastructure(CreateConfiguration("cart"));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CartOptions>>().Value;

        Assert.Equal(7 * 24 * 60, options.DefaultTtlMinutes);
    }

    private static IConfiguration CreateConfiguration(string keyNamespace, int? defaultTtlMinutes = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{CartOptions.SectionName}:KeyNamespace"] = keyNamespace,
        };

        if (defaultTtlMinutes is int ttl)
            values[$"{CartOptions.SectionName}:DefaultTtlMinutes"] = ttl.ToString();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
