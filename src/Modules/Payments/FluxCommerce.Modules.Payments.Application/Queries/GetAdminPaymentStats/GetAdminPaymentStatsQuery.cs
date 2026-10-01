using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;

/// <summary>
/// Returns aggregate Payment counts grouped by status plus succeeded totals per
/// currency for the administrative overview. Computed from local state only;
/// no Stripe calls are made.
/// </summary>
public sealed record GetAdminPaymentStatsQuery
    : IQuery<Result<PaymentAdminStatsResponse>>;
