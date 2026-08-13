using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// API test host for the Phase 5.8 Inventory surface. Auth uses the real Identity pipeline backed by
/// SQLite; the Inventory DbContext is also backed by SQLite. The reservation expiration background
/// service is removed so tests are deterministic and never touch the SQL-Server-only expiration query.
/// </summary>
public sealed class InventoryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");

    private readonly SqliteConnection _inventoryConnection = new("Data Source=:memory:");

    public InMemoryEmailSender EmailSender { get; } = new();

    public InventoryWebApplicationFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            RemoveAll(services, typeof(IEmailSender));
            services.AddSingleton<IEmailSender>(EmailSender);

            var hostedServices = services
                .Where(d =>
                    d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(ReservationExpirationBackgroundService))
                .ToList();

            foreach (var descriptor in hostedServices)
            {
                services.Remove(descriptor);
            }

            RemoveDbContext(services, typeof(IdentityDbContext));
            RemoveDbContext(services, typeof(InventoryDbContext));

            var loggerDescriptors = services
                .Where(d => d.ServiceType == typeof(ILoggerFactory) || d.ServiceType == typeof(ILoggerProvider))
                .ToList();

            foreach (var descriptor in loggerDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddLogging();

            _identityConnection.Open();
            _inventoryConnection.Open();

            var sqliteProvider = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddLogging()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options => options
                .UseSqlite(_identityConnection)
                .UseInternalServiceProvider(sqliteProvider));

            services.AddDbContext<InventoryDbContext>(options => options
                .UseSqlite(_inventoryConnection)
                .UseInternalServiceProvider(sqliteProvider));

            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();

            scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureCreated();
            scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Registers an Admin-role account through the real application handler. Returns the user id so
    /// tests can resolve the verification token.
    /// </summary>
    public async Task<Guid> RegisterAdminAsync(string email, string password, string firstName, string lastName)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RegisterAdminCommand, Result<UserResponse>>>();
        var result = await handler.HandleAsync(
            new RegisterAdminCommand(email, password, firstName, lastName),
            CancellationToken.None);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to register admin: {result.Error.Code}");
        return result.Value!.Id;
    }

    public string GetVerificationToken(string email)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return dbContext.Set<User>()
            .AsNoTracking()
            .Single(user => user.Email.Value == email)
            .VerificationToken!.Value;
    }

    /// <summary>
    /// Seeds an inventory item together with a reservation in the requested status and deadline so a
    /// test can deterministically produce the states the reservation lookup endpoint observes.
    /// Returns the reservation's id.
    /// </summary>
    public async Task<Guid> SeedReservationAsync(
        string sku,
        int onHand,
        int quantity,
        DateTimeOffset expiresAt,
        InventoryReservationStatus status = InventoryReservationStatus.Pending)
    {
        var now = DateTimeOffset.UtcNow;
        var item = InventoryItem.Create(new InventorySku(sku.ToUpperInvariant()), onHand, now);

        switch (status)
        {
            case InventoryReservationStatus.Committed:
                item.Reserve(quantity, now);
                item.Commit(quantity, now);
                break;
            case InventoryReservationStatus.Released:
            case InventoryReservationStatus.Expired:
                item.Reserve(quantity, now);
                item.Release(quantity, now);
                break;
            case InventoryReservationStatus.Pending:
                item.Reserve(quantity, now);
                break;
        }

        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            quantity,
            status,
            expiresAt,
            InventoryReservation.InitialVersion,
            now,
            now);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        return reservation.Id.Value;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _identityConnection.Dispose();
            _inventoryConnection.Dispose();
        }
    }

    private static void RemoveAll(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static void RemoveDbContext(IServiceCollection services, Type dbContextType)
    {
        var descriptors = services
            .Where(d =>
                d.ServiceType == dbContextType
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)
                    && d.ServiceType.GetGenericArguments().Contains(dbContextType))
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                    && d.ServiceType.GetGenericArguments().Contains(dbContextType)))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
