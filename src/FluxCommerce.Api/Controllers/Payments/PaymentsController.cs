using FluxCommerce.Api.Constants;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetMyPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetPayment;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;

namespace FluxCommerce.Api.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Authorize]
[Tags(EndpointTags.Payments)]
public sealed class PaymentsController : ApiControllerBase
{
    private readonly IStripeWebhookEndpointHandler _webhookEndpointHandler;
    private readonly IQueryHandler<GetPaymentQuery, Result<PaymentAdminResponse>> _getPaymentHandler;
    private readonly IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>> _initializationHandler;
    private readonly IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> _myPaymentsHandler;

    public PaymentsController(
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer,
        IStripeWebhookEndpointHandler webhookEndpointHandler,
        IQueryHandler<GetPaymentQuery, Result<PaymentAdminResponse>> getPaymentHandler,
        IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>> initializationHandler,
        IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> myPaymentsHandler
    )
        : base(localizer, errorCodeLocalizer)
    {
        _webhookEndpointHandler = webhookEndpointHandler ?? throw new ArgumentNullException(nameof(webhookEndpointHandler));
        _getPaymentHandler = getPaymentHandler ?? throw new ArgumentNullException(nameof(getPaymentHandler));
        _initializationHandler = initializationHandler ?? throw new ArgumentNullException(nameof(initializationHandler));
        _myPaymentsHandler = myPaymentsHandler ?? throw new ArgumentNullException(nameof(myPaymentsHandler));
    }

    /// <summary>
    /// Lists the authenticated customer's own payments, newest first, with an optional status
    /// filter. Pagination falls back to page 1 with a page size of 20 (maximum 100).
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPayments(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var result = await _myPaymentsHandler.HandleAsync(
            new GetMyPaymentsQuery(pageNumber, pageSize, status),
            cancellationToken);

        return Success(result);
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

        var result = await _getPaymentHandler.HandleAsync(new GetPaymentQuery(paymentId), cancellationToken);

        if (result.IsSuccess)
        {
            activity?.SetTag("payment.order_id", result.Value!.OrderId);
            activity?.SetTag("payment.status", result.Value!.Status);
        }

        return Success(result);
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
        Request.EnableBuffering();

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
            Request.Body.Position = 0;
        }

        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        // 9.9-9.11: atomic idempotent processing + state mapping + Outbox. No Phase 10 saga.
        var outcome = await _webhookEndpointHandler.HandleAsync(payload, signatureHeader, cancellationToken);

        return outcome switch
        {
            WebhookEndpointResult.Processed or WebhookEndpointResult.AlreadyProcessed => Ok(),
            _ => BadRequest()
        };
    }
}

