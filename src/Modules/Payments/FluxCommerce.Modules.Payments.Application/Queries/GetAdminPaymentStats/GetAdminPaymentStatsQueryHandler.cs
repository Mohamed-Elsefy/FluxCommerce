using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;

/// <summary>
/// Returns aggregate Payment counts grouped by status plus succeeded totals per
/// currency for the administrative overview.
/// </summary>
public sealed class GetAdminPaymentStatsQueryHandler
    : IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>>
{
    private readonly IPaymentAdminStore _adminStore;

    public GetAdminPaymentStatsQueryHandler(IPaymentAdminStore adminStore)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
    }

    public async Task<Result<PaymentAdminStatsResponse>> HandleAsync(
        GetAdminPaymentStatsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stats = await _adminStore
            .GetStatsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PaymentAdminStatsResponse(
            stats.Total,
            stats.Pending,
            stats.Processing,
            stats.Succeeded,
            stats.Failed,
            stats.Cancelled,
            stats.Refunded,
            stats.TotalsByCurrency
                .Select(total => new PaymentCurrencyTotalResponse(
                    total.Currency,
                    total.SucceededCount,
                    total.SucceededTotalAmount))
                .ToList(),
            stats.RefundedTotalsByCurrency
                .Select(total => new PaymentCurrencyTotalResponse(
                    total.Currency,
                    total.SucceededCount,
                    total.SucceededTotalAmount))
                .ToList()));
    }
}
