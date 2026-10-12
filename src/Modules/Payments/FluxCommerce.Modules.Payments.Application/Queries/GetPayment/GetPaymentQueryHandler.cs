using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Validation;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPayment;

/// <summary>
/// Loads the requested Payment from the lightweight admin projection and enforces
/// customer ownership before mapping. A missing payment, a stranger's payment, or a
/// legacy row with an empty customer id all surface as the same not-found error so
/// callers can never probe for another customer's payment existence.
/// </summary>
public sealed class GetPaymentQueryHandler
    : IQueryHandler<GetPaymentQuery, Result<PaymentAdminResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IPaymentAdminStore _adminStore;

    public GetPaymentQueryHandler(ICurrentUser currentUser, IPaymentAdminStore adminStore)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
    }

    public async Task<Result<PaymentAdminResponse>> HandleAsync(
        GetPaymentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payment = await _adminStore
            .GetByIdAsync(query.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
            return Result.Failure<PaymentAdminResponse>(PaymentAdminErrors.NotFound);

        // Fail closed: an empty customer id never becomes readable, and any caller
        // mismatch (including an unauthenticated caller) is masked as not-found.
        if (payment.CustomerId == Guid.Empty || _currentUser.UserId != payment.CustomerId)
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
