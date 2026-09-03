using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;

/// <summary>
/// Phase 8.2 <see cref="IOrderSagaStore"/> backed by the Orders DbContext. Lookups return the tracked
/// saga so a transition can be applied and flushed by the caller; <see cref="Add"/> only stages the
/// saga in the current unit of work because the idempotent-processing boundary (Phase 8.3) commits the
/// saga update atomically with the processed-message record.
/// </summary>
public sealed class OrderSagaStore : IOrderSagaStore
{
    private readonly OrdersDbContext _context;

    public OrderSagaStore(OrdersDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OrderSaga?> GetByOrderIdAsync(OrderId orderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        return await _context.OrderSagas
            .FirstOrDefaultAsync(saga => saga.OrderId == orderId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrderSaga?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderSagas
            .FirstOrDefaultAsync(saga => saga.CorrelationId == correlationId, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(OrderSaga saga)
    {
        ArgumentNullException.ThrowIfNull(saga);

        _context.OrderSagas.Add(saga);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}