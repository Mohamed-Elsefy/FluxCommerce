using FluxCommerce.Modules.Payments.Application.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public interface IWebhookEventProcessor
{
    /// <summary>
    /// Atomically records the Stripe webhook event as processed and applies the Payment state transition.
    /// Returns AlreadyProcessed if StripeEventId was seen before.
    /// Unsupported/unknown events are recorded as processed without state change.
    /// Terminal-state violations are caught and treated as success (no regression).
    /// When the event carries an amount/currency (<paramref name="amountMinor"/>, <paramref name="currency"/>),
    /// it must match the local Payment; a mismatch is recorded as processed with an error log and applies
    /// no transition and no outbox (Stripe stops retrying; the discrepancy is operational, not transient).
    /// </summary>
    Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, long? amountMinor = null, string? currency = null, string? failureReason = null, CancellationToken cancellationToken = default);

    // Backward compat: callers without amount details skip amount verification.
    Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, CancellationToken cancellationToken);

    // Backward compat for 9.9 tests that call 2-arg overload
    Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, CancellationToken cancellationToken = default);
}
