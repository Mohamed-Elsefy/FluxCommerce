using FluxCommerce.BuildingBlocks.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;

public sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    private const string ConnectionStringKey = "Database:ModuleConnections:Catalog";

    public CatalogDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<CatalogDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[ConnectionStringKey]
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringKey}' is not configured. " +
                "Set it via user secrets (e.g. 'dotnet user-secrets set \"Database:ModuleConnections:Catalog\" \"<connection-string>\"') or an environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Catalog");
        });

        return new CatalogDbContext(
            optionsBuilder.Options,
            new DesignTimeDateTimeProvider(),
            new DesignTimeCurrentUser(),
            new DesignTimeSaveChangesInterceptor());
    }
}
