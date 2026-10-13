using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.ReadModels;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPaymentDailyPoints;

/// <summary>
/// Daily-series projection for Payments: lightweight amount/currency/status rows within the
/// trailing window. <c>Days</c> is validated by the caller (dashboard contract: 1..90); values
/// below 1 are rejected here as a defense-in-depth guard. Authorization is enforced at the API
/// boundary. No Stripe calls are made.
/// </summary>
public sealed record GetPaymentDailyPointsQuery(
    int Days)
    : IQuery<Result<IReadOnlyList<PaymentDailyPointReadModel>>>;
