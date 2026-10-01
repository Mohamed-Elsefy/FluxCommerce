using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IOrderStore"/> backed by the Orders DbContext. The page is produced in two steps so it
/// stays portable across providers: only lightweight sort keys stream from the database and the
/// newest-first top-K selection runs in a bounded window (SQLite cannot order by a
/// <c>DateTimeOffset</c> column), then only the page's Orders are fetched through a
/// projection that sums the owned line items without materializing them. Single-order reads load the
/// tracked aggregate so a cancellation can be applied and flushed through the same context.
/// </summary>
public sealed class OrderStore : IOrderStore
{
    private static readonly Comparer<OrderSortKey> NewestFirstComparer = Comparer<OrderSortKey>.Create(
        static (left, right) =>
        {
            var byCreated = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
            return byCreated != 0
                ? byCreated
                : left.OrderId.Value.CompareTo(right.OrderId.Value);
        });

    private readonly OrdersDbContext _context;

    public OrderStore(OrdersDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<OrderSummaryReadModel>> GetMyOrdersPageAsync(
        CustomerId customerId,
        int pageNumber,
        int pageSize,
        OrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == customerId);

        if (status.HasValue)
            baseQuery = baseQuery.Where(order => order.Status == status.Value);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var skip = (pageNumber - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<OrderSummaryReadModel>([], totalCount, pageNumber, pageSize);

        // Bounded newest-first window: at most skip+take keys in memory regardless of history size.
        var windowSize = Math.Min(skip + pageSize, totalCount);
        var window = new List<OrderSortKey>(windowSize);

        await foreach (var key in baseQuery
            .Select(order => new OrderSortKey(order.Id, order.CreatedAtUtc))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var index = window.BinarySearch(key, NewestFirstComparer);
            if (index < 0)
                index = ~index;

            if (index < windowSize)
            {
                window.Insert(index, key);
                if (window.Count > windowSize)
                    window.RemoveAt(window.Count - 1);
            }
        }

        var pageKeys = window.Skip(skip).Take(pageSize).ToList();

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

    /// <summary>
    /// Administrative list with optional filters. Uses the same bounded newest-first window as
    /// the customer list so it stays portable across providers (SQLite cannot order by a
    /// <c>DateTimeOffset</c> column): only lightweight sort keys stream from the database.
    /// </summary>
    public async Task<PagedResult<OrderSummaryReadModel>> GetAdminOrdersPageAsync(
        OrderStatus? status,
        Guid? customerId,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = ApplyAdminFilters(
            _context.Orders.AsNoTracking(),
            status,
            customerId,
            search,
            from,
            to);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var skip = (pageNumber - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<OrderSummaryReadModel>([], totalCount, pageNumber, pageSize);

        // Bounded newest-first window: at most skip+take keys in memory regardless of history size.
        var windowSize = Math.Min(skip + pageSize, totalCount);
        var window = new List<OrderSortKey>(windowSize);

        await foreach (var key in baseQuery
            .Select(order => new OrderSortKey(order.Id, order.CreatedAtUtc))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var index = window.BinarySearch(key, NewestFirstComparer);
            if (index < 0)
                index = ~index;

            if (index < windowSize)
            {
                window.Insert(index, key);
                if (window.Count > windowSize)
                    window.RemoveAt(window.Count - 1);
            }
        }

        var pageKeys = window.Skip(skip).Take(pageSize).ToList();

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

    public async Task<AdminOrderCountsReadModel> GetAdminOrderCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await _context.Orders
            .AsNoTracking()
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = 0;
        var confirmed = 0;
        var cancelled = 0;

        foreach (var group in groups)
        {
            switch (group.Status)
            {
                case OrderStatus.Pending:
                    pending = group.Count;
                    break;
                case OrderStatus.Confirmed:
                    confirmed = group.Count;
                    break;
                case OrderStatus.Cancelled:
                    cancelled = group.Count;
                    break;
            }
        }

        return new AdminOrderCountsReadModel(
            pending + confirmed + cancelled,
            pending,
            confirmed,
            cancelled);
    }

    private static IQueryable<Order> ApplyAdminFilters(
        IQueryable<Order> query,
        OrderStatus? status,
        Guid? customerId,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (status.HasValue)
            query = query.Where(order => order.Status == status.Value);

        if (customerId.HasValue)
        {
            var scopedCustomer = new CustomerId(customerId.Value);
            query = query.Where(order => order.CustomerId == scopedCustomer);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(order => order.OrderNumber.Value.Contains(term));
        }

        if (from.HasValue)
            query = query.Where(order => order.CreatedAtUtc >= from.Value);

        if (to.HasValue)
            query = query.Where(order => order.CreatedAtUtc <= to.Value);

        return query;
    }

    private sealed record OrderSortKey(OrderId OrderId, DateTimeOffset CreatedAtUtc);
}
