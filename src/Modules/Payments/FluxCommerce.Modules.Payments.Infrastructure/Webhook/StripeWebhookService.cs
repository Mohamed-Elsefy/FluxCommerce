using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Stripe;
using System.Text.Json;

namespace FluxCommerce.Modules.Payments.Infrastructure.Webhook;

public sealed class StripeWebhookService : IWebhookVerifier
{
    private readonly string _webhookSecret;

    public StripeWebhookService(IOptions<PaymentGatewayOptions> stripeOptions)
    {
        _webhookSecret = stripeOptions.Value.WebhookSecret ?? throw new InvalidOperationException("Stripe Webhook Secret is not configured.");
    }

    public StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Stripe-Signature header is missing");
        }

        if (string.IsNullOrEmpty(payload))
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Payload is empty");
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret, tolerance: 300, throwOnApiVersionMismatch: false);

            var (paymentIntentId, amountMinor, currency, failureReason) = ExtractPaymentDetails(payload);

            return new StripeWebhookVerificationResult(true, stripeEvent.Id, stripeEvent.Type, paymentIntentId, null, amountMinor, currency, failureReason);
        }
        catch (StripeException)
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Invalid Stripe webhook signature");
        }
        catch (JsonException)
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Malformed webhook payload");
        }
    }

    private static string? ExtractPaymentIntentId(string payload) =>
        ExtractPaymentDetails(payload).PaymentIntentId;

    /// <summary>
    /// Extracts the payment intent id, the authoritative charged amount and the decline reason
    /// from the raw payload (<c>data.object</c>). <c>amount_received</c> is preferred (settled money);
    /// <c>amount</c> is the fallback for non-settled states. All amounts are Stripe minor
    /// units. The decline reason prefers <c>last_payment_error.decline_code</c>, then
    /// <c>last_payment_error.code</c>, then its message. Missing values yield nulls — the processor
    /// then skips amount verification with a warning instead of rejecting the event.
    /// </summary>
    private static (string? PaymentIntentId, long? AmountMinor, string? Currency, string? FailureReason) ExtractPaymentDetails(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj))
            {
                string? intentId = null;
                if (obj.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id) && id.StartsWith("pi_", StringComparison.Ordinal))
                        intentId = id;
                }

                long? amount = null;
                if (obj.TryGetProperty("amount_received", out var received) &&
                    received.ValueKind == JsonValueKind.Number &&
                    received.TryGetInt64(out var receivedValue))
                {
                    amount = receivedValue;
                }
                else if (obj.TryGetProperty("amount", out var authorized) &&
                    authorized.ValueKind == JsonValueKind.Number &&
                    authorized.TryGetInt64(out var authorizedValue))
                {
                    amount = authorizedValue;
                }

                string? currency = null;
                if (obj.TryGetProperty("currency", out var currencyProp) &&
                    currencyProp.ValueKind == JsonValueKind.String)
                {
                    currency = currencyProp.GetString();
                }

                string? failureReason = null;
                if (obj.TryGetProperty("last_payment_error", out var paymentError) &&
                    paymentError.ValueKind == JsonValueKind.Object)
                {
                    failureReason = ReadErrorField(paymentError, "decline_code")
                        ?? ReadErrorField(paymentError, "code")
                        ?? ReadErrorField(paymentError, "message");
                }

                return (intentId, amount, currency, failureReason);
            }
        }
        catch { }
        return (null, null, null, null);
    }

    private static string? ReadErrorField(JsonElement error, string field) =>
        error.TryGetProperty(field, out var prop) &&
        prop.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(prop.GetString())
            ? prop.GetString()
            : null;
}

