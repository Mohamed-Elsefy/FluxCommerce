using FluxCommerce.BuildingBlocks.Persistence;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;

public sealed class InventoryDbContext : BaseDbContext
{
    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options, dateTimeProvider, currentUser, saveChangesInterceptor)
    {
    }
}
