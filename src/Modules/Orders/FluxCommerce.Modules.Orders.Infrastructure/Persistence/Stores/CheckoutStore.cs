using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="ICheckoutStore"/> backed by the Orders DbContext. The Order snapshot and its
/// (Customer, Idempotency-Key) record are added to the same DbContext and flushed with a single
/// <c>SaveChangesAsync</c>, so the checkout is atomic. When the unique index on (UserId,
/// IdempotencyKey) rejects a concurrent duplicate, the winning record is re-read and returned so the
/// handler can resolve the retry or report an idempotency conflict.
/// </summary>
public sealed class CheckoutStore : ICheckoutStore
{
    private readonly OrdersDbContext _context;

    public CheckoutStore(OrdersDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CheckoutIdempotencyRecord?> FindIdempotencyRecordAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.CheckoutIdempotencies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                record => record.UserId == userId && record.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<Order?> GetOrderAsync(OrderId orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
            .Include(order => order.LineItems)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CheckoutPersistResult> PersistAsync(
        Order order,
        CheckoutIdempotencyRecord idempotency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(idempotency);

        _context.Orders.Add(order);
        _context.CheckoutIdempotencies.Add(CheckoutIdempotency.Create(
            idempotency.Id,
            idempotency.UserId,
            idempotency.IdempotencyKey,
            idempotency.OrderId,
            idempotency.RequestFingerprint,
            idempotency.CreatedAtUtc));

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CheckoutPersistResult(CheckoutPersistStatus.Created, null);
        }
        catch (DbUpdateException)
        {
            var existing = await FindIdempotencyRecordAsync(
                idempotency.UserId,
                idempotency.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);

            if (existing is null)
                throw;

            _context.ChangeTracker.Clear();

            return new CheckoutPersistResult(CheckoutPersistStatus.Duplicate, existing);
        }
    }

    private static CheckoutIdempotencyRecord Map(CheckoutIdempotency entity) =>
        new(
            entity.Id,
            entity.UserId,
            entity.IdempotencyKey,
            entity.OrderId,
            entity.RequestFingerprint,
            entity.CreatedAtUtc);
}
