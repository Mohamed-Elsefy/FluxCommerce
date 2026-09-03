using FluxCommerce.Modules.Payments.Application.Enums;

namespace FluxCommerce.Modules.Payments.Application.Services;

/// <summary>
/// Application-layer translation of external Stripe webhook event types into
/// system-understood <see cref="PaymentWebhookAction"/> values.
/// No Stripe SDK, no persistence, no Domain mutation — pure interpretation.
/// </summary>
public interface IPaymentWebhookInterpreter
{
    PaymentWebhookAction Interpret(string? eventType);
}
