using FluxCommerce.Modules.Payments.Application.Enums;

namespace FluxCommerce.Modules.Payments.Application.Services;

/// <summary>
/// Default interpreter: maps Stripe webhook event types to <see cref="PaymentWebhookAction"/>.
/// This is the sole place where external Stripe strings are understood by the system;
/// Infrastructure (WebhookEventProcessor) delegates to this Application service before
/// invoking any Domain state transition.
/// </summary>
public sealed class PaymentWebhookInterpreter : IPaymentWebhookInterpreter
{
    public PaymentWebhookAction Interpret(string? eventType) => eventType switch
    {
        "payment_intent.succeeded" => PaymentWebhookAction.Succeed,
        "payment_intent.payment_failed" => PaymentWebhookAction.Fail,
        "payment_intent.canceled" => PaymentWebhookAction.Cancel,
        "payment_intent.processing" => PaymentWebhookAction.Process,
        _ => PaymentWebhookAction.Ignore
    };
}
