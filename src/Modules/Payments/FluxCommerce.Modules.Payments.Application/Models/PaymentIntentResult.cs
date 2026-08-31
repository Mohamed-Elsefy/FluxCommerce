using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Models;

public sealed record PaymentIntentResult(
    string StripePaymentIntentId,
    PaymentStatus Status,
    string? ClientSecret);
