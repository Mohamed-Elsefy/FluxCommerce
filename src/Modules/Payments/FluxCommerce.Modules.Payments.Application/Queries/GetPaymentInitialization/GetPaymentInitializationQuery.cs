using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Contracts.Responses;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;

public sealed record GetPaymentInitializationQuery(Guid OrderId) : IQuery<Result<PaymentInitializationResponse>>;
