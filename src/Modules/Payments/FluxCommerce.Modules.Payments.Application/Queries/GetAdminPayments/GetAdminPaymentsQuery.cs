using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayments;

/// <summary>
/// Administrative Payment list: all payments, newest first, with optional filters.
/// Authorization is enforced at the API boundary (<c>AdminPolicy</c>). Status is the
/// <c>PaymentStatus</c> name (<c>Pending|Processing|Succeeded|Failed|Cancelled|Refunded</c>,
/// case-insensitive). No Stripe calls are made.
/// </summary>
public sealed record GetAdminPaymentsQuery(
    int? PageNumber,
    int? PageSize,
    string? Status,
    Guid? OrderId,
    Guid? CustomerId,
    DateTimeOffset? From,
    DateTimeOffset? To)
    : IQuery<Result<PagedResult<PaymentAdminResponse>>>;
