using FluxCommerce.Modules.Payments.Application.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public interface IWebhookEventProcessor
{
    /// <summary>
    /// Atomically records the Stripe webhook event as processed and applies the Payment state transition.
    /// Returns AlreadyProcessed if StripeEventId was seen before.
    /// Unsupported/unknown events are recorded as processed without state change.
    /// Terminal-state violations are caught and treated as success (no regression).
    /// </summary>
    Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, CancellationToken cancellationToken = default);

    // Backward compat for 9.9 tests that call 2-arg overload
    Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, CancellationToken cancellationToken = default);
}
