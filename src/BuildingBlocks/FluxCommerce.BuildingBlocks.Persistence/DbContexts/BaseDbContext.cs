using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.BuildingBlocks.Persistence.DbContexts;

public abstract class BaseDbContext : DbContext
{
    protected readonly IDateTimeProvider DateTimeProvider;
    protected readonly ICurrentUser CurrentUser;

    private readonly ISaveChangesInterceptor _saveChangesInterceptor;

    protected BaseDbContext(
        DbContextOptions options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options)
    {
        DateTimeProvider = dateTimeProvider;
        CurrentUser = currentUser;
        _saveChangesInterceptor = saveChangesInterceptor;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_saveChangesInterceptor);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddPersistenceConventions();
        base.ConfigureConventions(configurationBuilder);
    }
}
