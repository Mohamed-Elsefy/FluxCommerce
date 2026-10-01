using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Validation;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayment;

/// <summary>
/// Loads the requested Payment by identity alone (no customer scoping) and maps it
/// to the administrative response. A missing Payment surfaces as not-found.
/// </summary>
public sealed class GetAdminPaymentQueryHandler
    : IQueryHandler<GetAdminPaymentQuery, Result<PaymentAdminResponse>>
{
    private readonly IPaymentAdminStore _adminStore;

    public GetAdminPaymentQueryHandler(IPaymentAdminStore adminStore)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
    }

    public async Task<Result<PaymentAdminResponse>> HandleAsync(
        GetAdminPaymentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payment = await _adminStore
            .GetByIdAsync(query.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
            return Result.Failure<PaymentAdminResponse>(PaymentAdminErrors.NotFound);

        return Result.Success(new PaymentAdminResponse(
            payment.Id,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.ProviderPaymentIntentId,
            payment.Provider,
            payment.CreatedAtUtc,
            payment.UpdatedAtUtc,
            payment.Version));
    }
}
