using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
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

    public DbSet<InventoryItem> InventoryItems { get; set; } = null!;

    public DbSet<InventoryReservation> InventoryReservations { get; set; } = null!;

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    public DbSet<ProcessedMessage> ProcessedMessages { get; set; } = null!;
}
