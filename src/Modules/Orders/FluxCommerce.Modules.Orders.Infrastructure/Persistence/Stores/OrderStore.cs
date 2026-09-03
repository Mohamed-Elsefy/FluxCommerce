using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IOrderStore"/> backed by the Orders DbContext. The page is produced in two steps so it
/// stays portable across providers: the newest-first page keys are ordered in memory (SQLite cannot
/// order by a <c>DateTimeOffset</c> column), then only the page's Orders are fetched through a
/// projection that sums the owned line items without materializing them. Single-order reads load the
/// tracked aggregate so a cancellation can be applied and flushed through the same context.
/// </summary>
public sealed class OrderStore : IOrderStore
{
    private readonly OrdersDbContext _context;

    public OrderStore(OrdersDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<OrderSummaryReadModel>> GetMyOrdersPageAsync(
        CustomerId customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == customerId);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var pageKeys = (await baseQuery
                .Select(order => new OrderSortKey(order.Id, order.CreatedAtUtc))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(key => key.CreatedAtUtc)
            .ThenBy(key => key.OrderId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageOrderIds = pageKeys.Select(key => key.OrderId).ToList();

        var items = await baseQuery
            .Where(order => pageOrderIds.Contains(order.Id))
            .Select(order => new OrderSummaryReadModel(
                order.Id,
                order.OrderNumber,
                order.Status,
                order.LineItems.Sum(item => item.UnitPrice.Amount * item.Quantity)
                    + order.ShippingCost.Amount
                    - order.Discount.Amount,
                order.ShippingCost.Currency,
                order.LineItems.Count,
                order.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemByOrderId = items.ToDictionary(item => item.OrderId);

        var orderedItems = pageKeys
            .Select(key => itemByOrderId[key.OrderId])
            .ToList();

        return new PagedResult<OrderSummaryReadModel>(orderedItems, totalCount, pageNumber, pageSize);
    }

    public async Task<Order?> GetOwnedOrderAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(order => order.LineItems)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.CustomerId == customerId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(order => order.LineItems)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    private sealed record OrderSortKey(OrderId OrderId, DateTimeOffset CreatedAtUtc);
}
