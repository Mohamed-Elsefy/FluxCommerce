using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.DbContexts;

public sealed class PaymentsDbContext : BaseDbContext
{
    public PaymentsDbContext(
        DbContextOptions<PaymentsDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        ISaveChangesInterceptor saveChangesInterceptor)
        : base(options, dateTimeProvider, currentUser, saveChangesInterceptor)
    {
    }

    public DbSet<Payment> Payments {  get; set; } = null!;
}
