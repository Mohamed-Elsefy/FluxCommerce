using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPayment;

/// <summary>
/// Reads a single Payment owned by the authenticated customer.
/// Ownership is enforced inside the handler with a masked not-found:
/// another customer's payment (or a legacy row with an empty customer id)
/// is indistinguishable from a missing one.
/// </summary>
public sealed record GetPaymentQuery(Guid PaymentId)
    : IQuery<Result<PaymentAdminResponse>>;
