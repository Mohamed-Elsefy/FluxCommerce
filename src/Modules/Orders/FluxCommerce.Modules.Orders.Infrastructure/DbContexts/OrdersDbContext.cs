using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
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

    public DbSet<Order> Orders { get; set; } = null!;

    public DbSet<CheckoutIdempotency> CheckoutIdempotencies { get; set; } = null!;

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    public DbSet<ProcessedMessage> ProcessedMessages { get; set; } = null!;

    public DbSet<OrderSummaryProjectionEntity> OrderSummaryProjections { get; set; } = null!;
}
