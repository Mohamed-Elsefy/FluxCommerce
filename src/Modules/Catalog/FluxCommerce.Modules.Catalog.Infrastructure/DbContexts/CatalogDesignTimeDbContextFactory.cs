using FluxCommerce.BuildingBlocks.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;

public sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0
            ? args[0]
            : "Server=localhost;Database=FluxCommerce_Catalog;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new CatalogDbContext(
            optionsBuilder.Options,
            new DesignTimeDateTimeProvider(),
            new DesignTimeCurrentUser(),
            new DesignTimeSaveChangesInterceptor());
    }
}
