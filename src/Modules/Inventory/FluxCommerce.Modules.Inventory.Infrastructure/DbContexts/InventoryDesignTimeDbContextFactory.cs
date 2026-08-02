using FluxCommerce.BuildingBlocks.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;

public sealed class InventoryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    private const string ConnectionStringKey = "Database:ModuleConnections:Inventory";

    public InventoryDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<InventoryDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[ConnectionStringKey]
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringKey}' is not configured. " +
                "Set it via user secrets (e.g. 'dotnet user-secrets set \"Database:ModuleConnections:Inventory\" \"<connection-string>\"') or an environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Inventory");
        });

        return new InventoryDbContext(
            optionsBuilder.Options,
            new DesignTimeDateTimeProvider(),
            new DesignTimeCurrentUser(),
            new DesignTimeSaveChangesInterceptor());
    }
}
