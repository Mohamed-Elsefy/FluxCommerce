using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class OrdersInfrastructureRegistrationTests
{
    [Fact]
    public void AddOrdersInfrastructure_RegistersOrdersDbContext_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(OrdersDbContext))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(OrdersDbContext), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddOrdersInfrastructure_ConfiguresSqlServer_WithOrdersMigrationsHistoryTable()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<DbContextOptions<OrdersDbContext>>();

        var relationalExtension = options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault();

        Assert.NotNull(relationalExtension);
        Assert.Equal("__EFMigrationsHistory_Orders", relationalExtension!.MigrationsHistoryTableName);
        Assert.Equal(typeof(OrdersDbContext).Assembly.GetName().Name, relationalExtension.MigrationsAssembly);
    }

    [Fact]
    public void AddOrdersInfrastructure_ResolvesOrdersDbContext()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, FakeDateTimeProvider>();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, FakeSaveChangesInterceptor>();
        services.AddOrdersInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<OrdersDbContext>();

        Assert.NotNull(context);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public void AddOrdersInfrastructure_RegistersOutboxDispatcher_AsScopedAndHosted()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(OrdersOutboxDispatcher)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(OrdersOutboxDispatcher));
    }

    [Fact]
    public void AddOrdersInfrastructure_RegistersOutboxDispatchOptions_WithDefaults()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxDispatchOptions>>().Value;

        Assert.Equal(50, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ClaimTimeout);
        Assert.Equal(5, options.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(1), options.BaseRetryDelay);
        Assert.Equal(2.0, options.RetryDelayMultiplier);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
    }

    [Fact]
    public void AddOrdersInfrastructure_RegistersSagaOrchestrator_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(OrderSagaOrchestrator)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddOrdersInfrastructure_RegistersSagaOutboxMessageFactory_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(SagaOutboxMessageFactory)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddOrdersInfrastructure_RegistersReservationRequestOptions_WithDefaults()
    {
        var services = new ServiceCollection();

        services.AddOrdersInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ReservationRequestOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(30), options.ReservationLifetime);
    }

    private const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Orders;User Id=sa;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
