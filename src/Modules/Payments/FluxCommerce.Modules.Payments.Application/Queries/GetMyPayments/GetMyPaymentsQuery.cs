using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetMyPayments;

/// <summary>
/// Lists the authenticated customer's own payments, newest first, with an optional status
/// filter. Status is the <c>PaymentStatus</c> name
/// (<c>Pending|Processing|Succeeded|Failed|Cancelled|Refunded</c>, case-insensitive).
/// Ownership is enforced by scoping to the caller's customer id; read-only, no Stripe calls.
/// </summary>
public sealed record GetMyPaymentsQuery(int? PageNumber, int? PageSize, string? Status)
    : IQuery<Result<PagedResult<PaymentAdminResponse>>>;
