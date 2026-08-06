using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests;

public sealed class CatalogWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");

    private readonly SqliteConnection _catalogConnection = new("Data Source=:memory:");

    public InMemoryEmailSender EmailSender { get; } = new();

    public CatalogWebApplicationFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var emailSenderDescriptors = services
                .Where(d => d.ServiceType == typeof(IEmailSender))
                .ToList();

            foreach (var descriptor in emailSenderDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IEmailSender>(EmailSender);

            RemoveDbContext(services, typeof(IdentityDbContext));

            RemoveDbContext(services, typeof(CatalogDbContext));

            var loggerDescriptors = services
                .Where(d => d.ServiceType == typeof(ILoggerFactory) || d.ServiceType == typeof(ILoggerProvider))
                .ToList();

            foreach (var descriptor in loggerDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddLogging();

            _identityConnection.Open();
            _catalogConnection.Open();

            var sqliteProvider = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddLogging()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options => options
                .UseSqlite(_identityConnection)
                .UseInternalServiceProvider(sqliteProvider));

            services.AddDbContext<CatalogDbContext>(options => options
                .UseSqlite(_catalogConnection)
                .UseInternalServiceProvider(sqliteProvider));

            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();

            var identityDbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identityDbContext.Database.EnsureCreated();

            var catalogDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalogDbContext.Database.EnsureCreated();
        });
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _identityConnection.Dispose();
            _catalogConnection.Dispose();
        }
    }
}
