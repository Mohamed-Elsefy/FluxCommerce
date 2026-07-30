using FluxCommerce.BuildingBlocks.Persistence;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Orders.Infrastructure.DbContexts;

public sealed class OrdersDbContext : BaseDbContext
{
    public OrdersDbContext(
        DbContextOptions<OrdersDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options, dateTimeProvider, currentUser, saveChangesInterceptor)
    {
    }
}
