using FluxCommerce.BuildingBlocks.Persistence;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;

public sealed class CatalogDbContext : BaseDbContext
{
    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options, dateTimeProvider, currentUser, saveChangesInterceptor)
    {
    }
}
