using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayment;

/// <summary>
/// Reads a single Payment by identity without customer ownership scoping.
/// The caller must be authorized at the API boundary (<c>AdminPolicy</c>).
/// </summary>
public sealed record GetAdminPaymentQuery(Guid PaymentId)
    : IQuery<Result<PaymentAdminResponse>>;
