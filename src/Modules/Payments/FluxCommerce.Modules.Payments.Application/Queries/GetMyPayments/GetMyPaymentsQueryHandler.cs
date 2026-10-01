using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Validation;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetMyPayments;

/// <summary>
/// Lists the authenticated customer's own payments. The query is scoped to the caller's
/// customer id so another customer's payments are never observable (same masking rule as
/// the single-payment endpoints).
/// </summary>
public sealed class GetMyPaymentsQueryHandler
    : IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IPaymentAdminStore _adminStore;

    public GetMyPaymentsQueryHandler(ICurrentUser currentUser, IPaymentAdminStore adminStore)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
    }

    public async Task<Result<PagedResult<PaymentAdminResponse>>> HandleAsync(
        GetMyPaymentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result.Failure<PagedResult<PaymentAdminResponse>>(
                Error.Unauthorized("Payment.Unauthorized", "Authentication required."));

        var pageNumber = query.PageNumber ?? PaymentAdminPaginationRules.DefaultPageNumber;
        var pageSize = query.PageSize ?? PaymentAdminPaginationRules.DefaultPageSize;

        var validationError = PaymentAdminPaginationRules.Validate(pageNumber, pageSize);
        if (validationError is not null)
            return Result.Failure<PagedResult<PaymentAdminResponse>>(validationError);

        PaymentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<PaymentStatus>(query.Status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(PaymentStatus), parsed))
                return Result.Failure<PagedResult<PaymentAdminResponse>>(PaymentAdminErrors.ValidationFailed);

            status = parsed;
        }

        var result = await _adminStore
            .GetPageAsync(
                status,
                null,
                userId,
                null,
                null,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<PaymentAdminResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }

    private static PaymentAdminResponse ToResponse(ReadModels.PaymentAdminReadModel payment) =>
        new(
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
            payment.Version);
}
