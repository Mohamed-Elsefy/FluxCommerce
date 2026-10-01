using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Validation;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayments;

/// <summary>
/// Administrative Payment list: validates pagination and the optional status filter,
/// then reads from the lightweight projection without ownership scoping.
/// </summary>
public sealed class GetAdminPaymentsQueryHandler
    : IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>>
{
    private readonly IPaymentAdminStore _adminStore;

    public GetAdminPaymentsQueryHandler(IPaymentAdminStore adminStore)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
    }

    public async Task<Result<PagedResult<PaymentAdminResponse>>> HandleAsync(
        GetAdminPaymentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

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

        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
            return Result.Failure<PagedResult<PaymentAdminResponse>>(PaymentAdminErrors.ValidationFailed);

        var result = await _adminStore
            .GetPageAsync(
                status,
                query.OrderId,
                query.CustomerId,
                query.From,
                query.To,
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
