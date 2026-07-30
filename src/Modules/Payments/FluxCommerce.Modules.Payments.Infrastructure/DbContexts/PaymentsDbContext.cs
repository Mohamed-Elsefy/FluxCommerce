using FluxCommerce.BuildingBlocks.Persistence;
using FluxCommerce.BuildingBlocks.Persistence.Configurations;
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
}
