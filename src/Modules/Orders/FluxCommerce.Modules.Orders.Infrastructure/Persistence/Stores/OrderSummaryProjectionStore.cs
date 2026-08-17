using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;

/// <summary>
/// Phase 7.9 <see cref="IOrderSummaryProjectionStore"/> backed by the Orders DbContext. It maps the
/// application read model onto the persistence row and stages it in the current unit of work; it never
/// saves on its own because the <c>IIdempotentProcessor</c> boundary commits the projection atomically
/// with the processed-message record.
/// </summary>
public sealed class OrderSummaryProjectionStore : IOrderSummaryProjectionStore
{
    private readonly OrdersDbContext _context;

    public OrderSummaryProjectionStore(OrdersDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Add(OrderSummaryProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        _context.OrderSummaryProjections.Add(OrderSummaryProjectionEntity.Create(
            projection.OrderId,
            projection.OrderNumber,
            projection.CustomerId,
            projection.Status,
            projection.GrandTotalAmount,
            projection.GrandTotalCurrency,
            projection.ItemCount,
            projection.CorrelationId,
            projection.CausationId,
            projection.CreatedAtUtc,
            projection.ProjectedAtUtc));
    }
}