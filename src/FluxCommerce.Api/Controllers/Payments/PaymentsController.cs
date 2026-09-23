using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using FluxCommerce.BuildingBlocks.Web.Responses;

namespace FluxCommerce.Api.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Authorize]
[Tags(EndpointTags.Payments)]
public sealed class PaymentsController : ApiControllerBase
{
    private readonly ILogger<PaymentsController> _logger;
    private readonly IStripeWebhookService _webhookService;
    private readonly IWebhookEventProcessor _webhookProcessor;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>> _initializationHandler;
    private readonly ICurrentUser _currentUser;

    public PaymentsController(
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer,
        ILogger<PaymentsController> logger,
        IStripeWebhookService webhookService,
        IWebhookEventProcessor webhookProcessor,
        IPaymentRepository paymentRepository,
        IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>> initializationHandler,
        ICurrentUser currentUser
    )
        : base(localizer, errorCodeLocalizer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
        _webhookProcessor = webhookProcessor ?? throw new ArgumentNullException(nameof(webhookProcessor));
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _initializationHandler = initializationHandler ?? throw new ArgumentNullException(nameof(initializationHandler));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    /// <summary>
    /// Retrieves a payment by identifier. Customer ownership is enforced.
    /// </summary>
    [HttpGet("{paymentId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = TraceContext.Source.StartActivity("Payments.GetPayment", ActivityKind.Server);
        activity?.SetTag("payment.id", paymentId);

        var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
            return ErrorResult(Error.NotFound("Payment.NotFound", "Payment not found."));

        // Ownership enforcement: 404 if another customer's payment (do not leak existence).
        // An empty CustomerId is denied as well (fail closed): legacy or malformed rows must
        // never become readable by any authenticated caller guessing the payment id.
        if (payment.CustomerId == Guid.Empty || _currentUser.UserId != payment.CustomerId)
        {
            _logger.LogWarning("Payment ownership violation. PaymentId={PaymentId}, Owner={Owner}, Caller={Caller}", paymentId, payment.CustomerId, _currentUser.UserId);
            return ErrorResult(Error.NotFound("Payment.NotFound", "Payment not found."));
        }

        activity?.SetTag("payment.order_id", payment.OrderId);
        activity?.SetTag("payment.status", payment.Status.ToString());

        return Success(Result.Success(new
        {
            payment.Id,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.Currency,
            Status = payment.Status.ToString(),
            payment.ProviderPaymentIntentId,
            payment.Provider,
            payment.CreatedAtUtc,
            payment.UpdatedAtUtc,
            payment.Version
        }));
    }

    /// <summary>
    /// Resolves the Stripe client secret for the owner of an order.
    /// Customer ownership is enforced with a masked 404. Returns 409 when the
    /// payment is not ready yet (saga still creating the intent — retry shortly)
    /// or can no longer be initialized (terminal state).
    /// </summary>
    /// <remarks>
    /// Read-only: never creates a Stripe intent (the saga owns creation). The live
    /// secret is re-fetched from the provider on every call, so retries reuse the
    /// single intent per order. The secret is runtime-only and must not be stored.
    /// </remarks>
    [HttpGet("by-order/{orderId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetInitializationByOrder(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var activity = TraceContext.Source.StartActivity("Payments.GetInitializationByOrder", ActivityKind.Server);
        activity?.SetTag("order.id", orderId);

        var result = await _initializationHandler.HandleAsync(new GetPaymentInitializationQuery(orderId), cancellationToken);

        if (result.IsSuccess)
            activity?.SetTag("payment.id", result.Value!.PaymentId);

        return Success(result);
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
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(1_000_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken = default)
    {
        using var activity = TraceContext.Source.StartActivity("Payments.Webhook", ActivityKind.Server);
        Request.EnableBuffering();

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
            Request.Body.Position = 0;
        }

        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();
        activity?.SetTag("stripe.signature_present", !string.IsNullOrEmpty(signatureHeader));

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
        activity?.SetTag("stripe.event_id", result.EventId);
        activity?.SetTag("stripe.event_type", result.EventType);
        activity?.SetTag("stripe.payment_intent_id", result.PaymentIntentId);
        activity?.SetTag("stripe.verified", result.IsValid);

        if (!result.IsValid)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {ErrorMessage}", result.ErrorMessage);
            return BadRequest();
        }

        // 9.9-9.11: atomic idempotent processing + state mapping + Outbox. No Phase 10 saga.
        var processResult = await _webhookProcessor.TryProcessAsync(result.EventId!, result.EventType, result.PaymentIntentId, result.AmountMinor, result.Currency, result.FailureReason, cancellationToken);
        activity?.SetTag("webhook.result", processResult.ToString());

        if (processResult == WebhookProcessResult.AlreadyProcessed)
        {
            _logger.LogInformation(
                "Stripe webhook duplicate detected, benign success. StripeEventId={StripeEventId}, EventType={EventType}, StripePaymentIntentId={StripePaymentIntentId}",
                result.EventId, result.EventType, result.PaymentIntentId);
            return Ok();
        }

        _logger.LogInformation(
            "Stripe webhook event processed. StripeEventId={StripeEventId}, EventType={EventType}, StripePaymentIntentId={StripePaymentIntentId}",
            result.EventId, result.EventType, result.PaymentIntentId);

        return Ok();
    }

    private IActionResult ErrorResult(Error error) => FromError(error);
}

