using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FluxCommerce.Api.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Tags("Payments")]
public sealed class PaymentsController : ApiControllerBase
{
    private readonly ILogger<PaymentsController> _logger;
    private readonly IStripeWebhookService _webhookService;
    private readonly IWebhookEventProcessor _webhookProcessor;

    public PaymentsController(
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer,
        ILogger<PaymentsController> logger,
        IStripeWebhookService webhookService,
        IWebhookEventProcessor webhookProcessor
    )
        : base(localizer, errorCodeLocalizer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
        _webhookProcessor = webhookProcessor ?? throw new ArgumentNullException(nameof(webhookProcessor));
    }

    /// <summary>
    /// Receives Stripe webhook events.
    /// </summary>
    /// <remarks>
    /// This endpoint is publicly accessible to Stripe servers; customer JWT authorization is not applied.
    /// The raw request body is required for Stripe signature verification.
    /// Invalid signatures are rejected with HTTP 400 Bad Request.
    /// Valid events are processed idempotently and a 200 OK response is returned.
    /// Duplicate deliveries return 200 OK so Stripe stops retrying.
    /// 9.10 state mapping and 9.11 Outbox/RabbitMQ are intentionally deferred.
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken = default)
    {
        Request.EnableBuffering();

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
            Request.Body.Position = 0;
        }

        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook missing Stripe-Signature header.");
            return BadRequest();
        }

        if (string.IsNullOrEmpty(payload))
        {
            _logger.LogWarning("Stripe webhook received with empty payload.");
            return BadRequest();
        }

        var result = _webhookService.VerifyAndParse(payload, signatureHeader);

        if (!result.IsValid)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {ErrorMessage}", result.ErrorMessage);
            return BadRequest();
        }

        // 9.9 + 9.10: atomic idempotent processing + explicit Stripe→Payment state mapping.
        // Supported events are applied to Payment aggregate; unsupported/out-of-order are logged and treated as success.
        // No Outbox/RabbitMQ yet (9.11 intentionally deferred).
        var processResult = await _webhookProcessor.TryProcessAsync(result.EventId!, result.EventType, result.PaymentIntentId, cancellationToken);

        if (processResult == WebhookProcessResult.AlreadyProcessed)
        {
            _logger.LogInformation(
                "Stripe webhook duplicate detected, benign success. StripeEventId={StripeEventId}, EventType={EventType}",
                result.EventId,
                result.EventType);
            return Ok();
        }

        _logger.LogInformation(
            "Stripe webhook event processed. StripeEventId={StripeEventId}, EventType={EventType}, PaymentIntentId={PaymentIntentId}",
            result.EventId,
            result.EventType,
            result.PaymentIntentId);

        return Ok();
    }
}
