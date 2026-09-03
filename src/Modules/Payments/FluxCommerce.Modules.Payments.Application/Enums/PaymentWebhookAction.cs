namespace FluxCommerce.Modules.Payments.Application.Enums;

/// <summary>
/// System-understood interpretation of an external Stripe webhook event.
/// Produced by the Application-layer interpreter (PaymentWebhookInterpreter) from the raw
/// Stripe event type, before any Domain state transition is attempted.
/// </summary>
public enum PaymentWebhookAction
{
    /// <summary>Event type is not supported — no state change, still recorded as processed.</summary>
    Ignore = 0,

    /// <summary>Stripe: payment_intent.succeeded → Domain Payment.Succeed</summary>
    Succeed = 1,

    /// <summary>Stripe: payment_intent.payment_failed → Domain Payment.Fail</summary>
    Fail = 2,

    /// <summary>Stripe: payment_intent.canceled → Domain Payment.Cancel</summary>
    Cancel = 3,

    /// <summary>Stripe: payment_intent.processing → Domain Payment.Process</summary>
    Process = 4
}
