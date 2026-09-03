using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Identity.Infrastructure.DbContexts;

public sealed class IdentityDbContext : BaseDbContext
{
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options, dateTimeProvider, currentUser, saveChangesInterceptor)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();
}
