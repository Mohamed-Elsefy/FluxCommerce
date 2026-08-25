using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Api.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Tags("Payments")]
public sealed class PaymentsController : ApiControllerBase
{
    private readonly ILogger<PaymentsController> _logger;
    private readonly IStripeWebhookService _webhookService;

    public PaymentsController(
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer,
        ILogger<PaymentsController> logger,
        IStripeWebhookService webhookService
    )
        : base(localizer, errorCodeLocalizer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
    }

    /// <summary>
    /// Receives Stripe webhook events.
    /// </summary>
    /// <remarks>
    /// This endpoint is publicly accessible to Stripe servers; customer JWT authorization is not applied.
    /// The raw request body is required for Stripe signature verification.
    /// Invalid signatures are rejected with HTTP 400 Bad Request.
    /// Valid events are logged and a 200 OK response is returned.
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(
        CancellationToken cancellationToken = default)
    {
        // Read the raw request body directly from the stream as string.
        // ASP.NET Core model binding would deserialize the body as JSON, which would alter the
        // binary content required for Stripe signature verification.
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook missing Stripe-Signature header.");
            return BadRequest();
        }

        var verificationResult = _webhookService.VerifyAndParse(payload, signatureHeader);

        if (!verificationResult.IsValid)
        {
            _logger.LogWarning("Stripe webhook verification failed: {ErrorMessage}", verificationResult.ErrorMessage);
            return BadRequest();
        }

        // At this point the event is verified and parsed.
        // TODO: 9.10 - Map Stripe event to Payment domain state transitions.
        // For 9.8, we simply log and return success; idempotent processing and state mapping
        // are deferred to sub-phases 9.9-9.10.

        _logger.LogInformation(
            "Stripe webhook event processed. EventId={EventId}",
            verificationResult.EventId);

        return Ok();
    }
}