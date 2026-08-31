namespace FluxCommerce.Modules.Payments.Application.Models;

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    long Version);
