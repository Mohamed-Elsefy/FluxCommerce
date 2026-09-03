using FluxCommerce.BuildingBlocks.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FluxCommerce.Modules.Orders.Infrastructure.DbContexts;

public sealed class OrdersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    private const string ConnectionStringKey = "Database:ModuleConnections:Orders";

    public OrdersDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<OrdersDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[ConnectionStringKey]
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringKey}' is not configured. " +
                "Set it via user secrets (e.g. 'dotnet user-secrets set \"Database:ModuleConnections:Orders\" \"<connection-string>\"') or an environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Orders");
        });

        return new OrdersDbContext(
            optionsBuilder.Options,
            new DesignTimeDateTimeProvider(),
            new DesignTimeCurrentUser(),
            new DesignTimeSaveChangesInterceptor());
    }
}
