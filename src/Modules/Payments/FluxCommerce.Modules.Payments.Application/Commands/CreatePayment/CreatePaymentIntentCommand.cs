using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;

namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed record CreatePaymentIntentCommand(Guid OrderId) : ICommand<Result<PaymentIntentResult>>;

