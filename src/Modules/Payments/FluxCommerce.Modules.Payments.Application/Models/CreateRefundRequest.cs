namespace FluxCommerce.Modules.Payments.Application.Models;

public sealed record CreateRefundRequest(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    decimal Amount,
    string Currency);
