namespace FluxCommerce.Modules.Payments.Application.Models;

public sealed record RefundResult(
    string StripeRefundId,
    string Status);
