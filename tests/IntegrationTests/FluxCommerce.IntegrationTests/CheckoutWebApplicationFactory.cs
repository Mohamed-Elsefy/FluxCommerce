using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// API test host for the Checkout flow. Auth uses the real Identity pipeline backed by SQLite; the
/// Catalog and Cart contracts are replaced with deterministic fakes so a Checkout is reproducible
/// without Redis; the Orders DbContext (used by <c>CheckoutStore</c>) is backed by SQLite so Order +
/// idempotency persistence is asserted end to end.
/// </summary>
public sealed class CheckoutWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");

    private readonly SqliteConnection _ordersConnection = new("Data Source=:memory:");

    public InMemoryEmailSender EmailSender { get; } = new();

    public FakeCartCheckoutSnapshotProvider CartSnapshotProvider { get; } = new();

    public FakeCatalogCheckoutLookup CatalogCheckoutLookup { get; } = new();

    public FakeCartCheckoutConsumption CartConsumption { get; } = new();

    public CheckoutWebApplicationFactory()
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

            RemoveAll(services, typeof(ICartCheckoutSnapshotProvider));
            services.AddSingleton<ICartCheckoutSnapshotProvider>(CartSnapshotProvider);

            RemoveAll(services, typeof(ICatalogVariantCheckoutLookup));
            services.AddSingleton<ICatalogVariantCheckoutLookup>(CatalogCheckoutLookup);

            RemoveAll(services, typeof(ICartCheckoutConsumption));
            services.AddSingleton<ICartCheckoutConsumption>(CartConsumption);

            RemoveDbContext(services, typeof(IdentityDbContext));
            RemoveDbContext(services, typeof(OrdersDbContext));

            var loggerDescriptors = services
                .Where(d => d.ServiceType == typeof(ILoggerFactory) || d.ServiceType == typeof(ILoggerProvider))
                .ToList();

            foreach (var descriptor in loggerDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddLogging();

            _identityConnection.Open();
            _ordersConnection.Open();

            var sqliteProvider = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddLogging()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options => options
                .UseSqlite(_identityConnection)
                .UseInternalServiceProvider(sqliteProvider));

            services.AddDbContext<OrdersDbContext>(options => options
                .UseSqlite(_ordersConnection)
                .UseInternalServiceProvider(sqliteProvider));

            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();

            scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureCreated();
            scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreated();
        });
    }

    public Guid GetUserId(string email)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var userId = dbContext.Set<User>()
            .AsNoTracking()
            .Single(user => user.Email.Value == email)
            .Id;

        return userId;
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

    public void ResetOrdersDatabase()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        dbContext.Database.ExecuteSqlRaw("DELETE FROM CheckoutIdempotency;");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM OrderItems;");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM Orders;");
    }

    public int CountOrders() => Count<Order>();

    public int CountIdempotencyRows() => Count<CheckoutIdempotency>();

    public string? GetOrderStatus(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        return dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefault(order => order.Id == new OrderId(orderId))?
            .Status.ToString();
    }

    private int Count<TEntity>() where TEntity : class
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return dbContext.Set<TEntity>().Count();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _identityConnection.Dispose();
            _ordersConnection.Dispose();
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