namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed record CreatePaymentIntentCommand(Guid OrderId);
