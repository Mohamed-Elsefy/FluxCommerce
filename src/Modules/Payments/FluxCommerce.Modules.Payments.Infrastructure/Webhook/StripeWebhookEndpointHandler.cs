using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Webhook;

/// <summary>
/// HTTP-delivery boundary for Stripe webhooks. Owns signature presence checks,
/// verification, idempotent application, and delivery logging so the API
/// controller stays a thin raw-body adapter.
/// </summary>
public sealed class StripeWebhookEndpointHandler : IWebhookEndpointHandler
{
    private readonly IWebhookVerifier _webhookService;
    private readonly IWebhookEventProcessor _webhookProcessor;
    private readonly ILogger<StripeWebhookEndpointHandler> _logger;

    public StripeWebhookEndpointHandler(
        IWebhookVerifier webhookService,
        IWebhookEventProcessor webhookProcessor,
        ILogger<StripeWebhookEndpointHandler> logger)
    {
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
        _webhookProcessor = webhookProcessor ?? throw new ArgumentNullException(nameof(webhookProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WebhookEndpointResult> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        using var activity = PaymentsActivitySource.Source.StartActivity("Payments.Webhook", ActivityKind.Server);
        activity?.SetTag("stripe.signature_present", !string.IsNullOrEmpty(signatureHeader));

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook missing Stripe-Signature header.");
            return WebhookEndpointResult.MissingSignature;
        }

        if (string.IsNullOrEmpty(payload))
        {
            _logger.LogWarning("Stripe webhook received with empty payload.");
            return WebhookEndpointResult.EmptyPayload;
        }

        var result = _webhookService.VerifyAndParse(payload, signatureHeader);
        activity?.SetTag("stripe.event_id", result.EventId);
        activity?.SetTag("stripe.event_type", result.EventType);
        activity?.SetTag("stripe.payment_intent_id", result.PaymentIntentId);
        activity?.SetTag("stripe.verified", result.IsValid);

        if (!result.IsValid)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {ErrorMessage}", result.ErrorMessage);
            return WebhookEndpointResult.InvalidSignature;
        }

        // Atomic idempotent processing + state mapping + Outbox. No saga logic here.
        var processResult = await _webhookProcessor.TryProcessAsync(
            result.EventId!,
            result.EventType,
            result.PaymentIntentId,
            result.AmountMinor,
            result.Currency,
            result.FailureReason,
            cancellationToken).ConfigureAwait(false);
        activity?.SetTag("webhook.result", processResult.ToString());

        if (processResult == WebhookProcessResult.AlreadyProcessed)
        {
            _logger.LogInformation(
                "Stripe webhook duplicate detected, benign success. StripeEventId={StripeEventId}, EventType={EventType}, StripePaymentIntentId={StripePaymentIntentId}",
                result.EventId, result.EventType, result.PaymentIntentId);
            return WebhookEndpointResult.AlreadyProcessed;
        }

        _logger.LogInformation(
            "Stripe webhook event processed. StripeEventId={StripeEventId}, EventType={EventType}, StripePaymentIntentId={StripePaymentIntentId}",
            result.EventId, result.EventType, result.PaymentIntentId);

        return WebhookEndpointResult.Processed;
    }
}
