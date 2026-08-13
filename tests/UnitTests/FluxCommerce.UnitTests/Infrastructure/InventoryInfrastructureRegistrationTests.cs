using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;
using FluxCommerce.Modules.Inventory.Application.Commands.CommitReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Commands.ExpireReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class InventoryInfrastructureRegistrationTests
{
    [Fact]
    public void AddInventoryInfrastructure_RegistersInventoryDbContext_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(InventoryDbContext))
            .ToList();

        Assert.Single(registrations);
        Assert.Equal(typeof(InventoryDbContext), registrations[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registrations[0].Lifetime);
    }

    [Fact]
    public void AddInventoryInfrastructure_ConfiguresSqlServer_WithInventoryMigrationsHistoryTable()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<DbContextOptions<InventoryDbContext>>();

        var relationalExtension = options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault();

        Assert.NotNull(relationalExtension);
        Assert.Equal("__EFMigrationsHistory_Inventory", relationalExtension!.MigrationsHistoryTableName);
        Assert.Equal(typeof(InventoryDbContext).Assembly.GetName().Name, relationalExtension.MigrationsAssembly);
    }

    [Fact]
    public void AddInventoryInfrastructure_ResolvesInventoryDbContext()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, FakeDateTimeProvider>();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, FakeSaveChangesInterceptor>();
        services.AddInventoryInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<InventoryDbContext>();

        Assert.NotNull(context);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersInventoryStore_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        var registration = services
            .Single(descriptor => descriptor.ServiceType == typeof(IInventoryStore));

        Assert.Equal(typeof(InventoryStore), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersStockManagementUseCases()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>>)
                && descriptor.ImplementationType == typeof(CreateInventoryItemCommandHandler));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<AdjustStockCommand, Result<InventoryItemReadModel>>)
                && descriptor.ImplementationType == typeof(AdjustStockCommandHandler));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IQueryHandler<GetInventoryItemQuery, Result<InventoryItemReadModel>>)
                && descriptor.ImplementationType == typeof(GetInventoryItemQueryHandler));
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersReservationLifecycleUseCases()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>)
                && descriptor.ImplementationType == typeof(ReserveInventoryCommandHandler));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>)
                && descriptor.ImplementationType == typeof(ReleaseReservationCommandHandler));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<CommitReservationCommand, Result<InventoryReservationReadModel>>)
                && descriptor.ImplementationType == typeof(CommitReservationCommandHandler));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICommandHandler<ExpireReservationCommand, Result<InventoryReservationReadModel>>)
                && descriptor.ImplementationType == typeof(ExpireReservationCommandHandler));
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersExpiredReservationProcessor_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        var registration = services
            .Single(descriptor => descriptor.ServiceType == typeof(IExpiredReservationProcessor));

        Assert.Equal(typeof(ExpiredReservationProcessor), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersExpirationBackgroundService_AsHostedService()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ReservationExpirationBackgroundService));
    }

    [Fact]
    public void AddInventoryInfrastructure_RegistersExpirationOptions_WithDefaults()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(ConnectionString);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ReservationExpirationOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(30), options.Interval);
        Assert.Equal(100, options.BatchSize);
    }

    [Fact]
    public void AddInventoryInfrastructure_ConfigureAction_OverridesExpirationOptions()
    {
        var services = new ServiceCollection();

        services.AddInventoryInfrastructure(
            ConnectionString,
            options =>
            {
                options.Interval = TimeSpan.FromSeconds(5);
                options.BatchSize = 7;
            });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ReservationExpirationOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.Interval);
        Assert.Equal(7, options.BatchSize);
    }

    private const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Inventory;User Id=sa;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
