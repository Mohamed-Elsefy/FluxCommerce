using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

/// <summary>
/// Persistence driven port for the administrative Payments queries. List and stats
/// queries are served from lightweight projections; no Stripe calls are made.
/// Authorization is enforced at the API boundary (<c>AdminPolicy</c>).
/// </summary>
public interface IPaymentAdminStore
{
    Task<PagedResult<PaymentAdminReadModel>> GetPageAsync(
        PaymentStatus? status,
        Guid? orderId,
        Guid? customerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PaymentAdminReadModel?> GetByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentAdminStatsReadModel> GetStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Daily-series projection: lightweight amount/currency/status rows for Payments created at
    /// or after <paramref name="fromUtc"/>. The date filter runs in memory over a lightweight
    /// projection stream (SQLite cannot translate <c>DateTimeOffset</c> comparisons); the
    /// succeeded-only revenue filter and day bucketing also run in memory at the consumer.
    /// </summary>
    Task<IReadOnlyList<PaymentDailyPointReadModel>> GetDailyPointsAsync(
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default);
}
