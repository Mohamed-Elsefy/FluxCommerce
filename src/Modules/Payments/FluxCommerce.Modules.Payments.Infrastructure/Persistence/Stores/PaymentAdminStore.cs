using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IPaymentAdminStore"/> backed by the Payments DbContext. List queries use a
/// bounded newest-first key window so they stay portable across providers (only
/// lightweight sort keys stream from the database); stats are computed in the
/// database via grouping.
/// </summary>
public sealed class PaymentAdminStore : IPaymentAdminStore
{
    private static readonly Comparer<PaymentSortKey> NewestFirstComparer = Comparer<PaymentSortKey>.Create(
        static (left, right) =>
        {
            var byCreated = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
            return byCreated != 0
                ? byCreated
                : left.PaymentId.CompareTo(right.PaymentId);
        });

    private readonly PaymentsDbContext _context;

    public PaymentAdminStore(PaymentsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<PaymentAdminReadModel>> GetPageAsync(
        PaymentStatus? status,
        Guid? orderId,
        Guid? customerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = ApplyFilters(
            _context.Payments.AsNoTracking(),
            status,
            orderId,
            customerId,
            from,
            to);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var skip = (pageNumber - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<PaymentAdminReadModel>([], totalCount, pageNumber, pageSize);

        // Bounded newest-first window: at most skip+take keys in memory regardless of history size.
        var windowSize = Math.Min(skip + pageSize, totalCount);
        var window = new List<PaymentSortKey>(windowSize);

        await foreach (var key in baseQuery
            .Select(payment => new PaymentSortKey(payment.Id, payment.CreatedAtUtc))
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

        var pagePaymentIds = pageKeys.Select(key => key.PaymentId).ToList();

        var items = await baseQuery
            .Where(payment => pagePaymentIds.Contains(payment.Id))
            .Select(payment => new PaymentAdminReadModel(
                payment.Id,
                payment.OrderId,
                payment.CustomerId,
                payment.Amount,
                payment.Currency,
                payment.Status,
                payment.ProviderPaymentIntentId,
                payment.Provider,
                payment.CreatedAtUtc,
                payment.UpdatedAtUtc,
                payment.Version))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemByPaymentId = items.ToDictionary(item => item.Id);

        var orderedItems = pageKeys
            .Select(key => itemByPaymentId[key.PaymentId])
            .ToList();

        return new PagedResult<PaymentAdminReadModel>(orderedItems, totalCount, pageNumber, pageSize);
    }

    public async Task<PaymentAdminReadModel?> GetByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => new PaymentAdminReadModel(
                payment.Id,
                payment.OrderId,
                payment.CustomerId,
                payment.Amount,
                payment.Currency,
                payment.Status,
                payment.ProviderPaymentIntentId,
                payment.Provider,
                payment.CreatedAtUtc,
                payment.UpdatedAtUtc,
                payment.Version))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaymentAdminStatsReadModel> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await _context.Payments
            .AsNoTracking()
            .GroupBy(payment => payment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = 0;
        var processing = 0;
        var succeeded = 0;
        var failed = 0;
        var cancelled = 0;
        var refunded = 0;

        foreach (var group in groups)
        {
            switch (group.Status)
            {
                case PaymentStatus.Pending:
                    pending = group.Count;
                    break;
                case PaymentStatus.Processing:
                    processing = group.Count;
                    break;
                case PaymentStatus.Succeeded:
                    succeeded = group.Count;
                    break;
                case PaymentStatus.Failed:
                    failed = group.Count;
                    break;
                case PaymentStatus.Cancelled:
                    cancelled = group.Count;
                    break;
                case PaymentStatus.Refunded:
                    refunded = group.Count;
                    break;
            }
        }

        var totalsByCurrency = await _context.Payments
            .AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Succeeded)
            .GroupBy(payment => payment.Currency)
            .Select(group => new PaymentCurrencyTotalReadModel(
                group.Key,
                group.Count(),
                group.Sum(payment => payment.Amount)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PaymentAdminStatsReadModel(
            pending + processing + succeeded + failed + cancelled + refunded,
            pending,
            processing,
            succeeded,
            failed,
            cancelled,
            refunded,
            totalsByCurrency);
    }

    private static IQueryable<Payment> ApplyFilters(
        IQueryable<Payment> query,
        PaymentStatus? status,
        Guid? orderId,
        Guid? customerId,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (status.HasValue)
            query = query.Where(payment => payment.Status == status.Value);

        if (orderId.HasValue)
            query = query.Where(payment => payment.OrderId == orderId.Value);

        if (customerId.HasValue)
            query = query.Where(payment => payment.CustomerId == customerId.Value);

        if (from.HasValue)
            query = query.Where(payment => payment.CreatedAtUtc >= from.Value);

        if (to.HasValue)
            query = query.Where(payment => payment.CreatedAtUtc <= to.Value);

        return query;
    }

    private sealed record PaymentSortKey(Guid PaymentId, DateTimeOffset CreatedAtUtc);
}
