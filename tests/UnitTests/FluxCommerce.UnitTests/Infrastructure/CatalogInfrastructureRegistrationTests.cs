using FluxCommerce.Modules.Catalog.Application.Services;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CatalogInfrastructureRegistrationTests
{
    [Fact]
    public void AddCatalogInfrastructure_RegistersCatalogVariantCartLookup()
    {
        var services = new ServiceCollection();

        services.AddCatalogInfrastructure(ConnectionString);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICatalogVariantCartLookup))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(CatalogVariantCartLookup), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddCatalogInfrastructure_RegistersCatalogVariantCheckoutLookup()
    {
        var services = new ServiceCollection();

        services.AddCatalogInfrastructure(ConnectionString);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICatalogVariantCheckoutLookup))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(CatalogVariantCheckoutLookup), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    private const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Catalog;User Id=sa;TrustServerCertificate=True;MultipleActiveResultSets=true";
}