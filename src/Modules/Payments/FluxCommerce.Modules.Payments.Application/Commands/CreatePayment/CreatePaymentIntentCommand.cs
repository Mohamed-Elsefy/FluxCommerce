using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;

namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed record CreatePaymentIntentCommand(Guid OrderId) : ICommand<Result<PaymentIntentResult>>;
