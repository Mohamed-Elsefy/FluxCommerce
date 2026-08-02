using FluxCommerce.BuildingBlocks.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FluxCommerce.Modules.Cart.Infrastructure.DbContexts;

public sealed class CartDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    private const string ConnectionStringKey = "Database:ModuleConnections:Cart";

    public CartDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<CartDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[ConnectionStringKey]
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringKey}' is not configured. " +
                "Set it via user secrets (e.g. 'dotnet user-secrets set \"Database:ModuleConnections:Cart\" \"<connection-string>\"') or an environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<CartDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Cart");
        });

        return new CartDbContext(
            optionsBuilder.Options,
            new DesignTimeDateTimeProvider(),
            new DesignTimeCurrentUser(),
            new DesignTimeSaveChangesInterceptor());
    }
}
