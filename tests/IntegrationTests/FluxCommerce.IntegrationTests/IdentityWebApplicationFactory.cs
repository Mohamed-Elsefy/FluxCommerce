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

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public InMemoryEmailSender EmailSender { get; } = new();

    public IdentityWebApplicationFactory()
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

            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(IdentityDbContext)
                    || d.ServiceType == typeof(DbContextOptions<IdentityDbContext>)
                    || (d.ServiceType.IsGenericType
                        && d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                        && d.ServiceType.GetGenericArguments().Contains(typeof(IdentityDbContext))))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            var loggerDescriptors = services
                .Where(d => d.ServiceType == typeof(ILoggerFactory) || d.ServiceType == typeof(ILoggerProvider))
                .ToList();

            foreach (var descriptor in loggerDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddLogging();

            _connection.Open();

            var sqliteProvider = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddLogging()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options => options
                .UseSqlite(_connection)
                .UseInternalServiceProvider(sqliteProvider));

            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            dbContext.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
