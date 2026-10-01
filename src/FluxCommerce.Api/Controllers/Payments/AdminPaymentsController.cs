using FluxCommerce.Api.Extensions;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayment;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Payments;

/// <summary>
/// Administrative Payments surface: full Payment list with filters, single Payment
/// inspection and aggregate stats. All endpoints require the Admin role; no customer
/// ownership scoping is applied. Read-only: no Stripe calls are made.
/// </summary>
[ApiController]
[Route("admin/payments")]
[Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
[Tags(EndpointTags.Payments)]
public sealed class AdminPaymentsController : ApiControllerBase
{
    private readonly IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> _getAdminPaymentsHandler;
    private readonly IQueryHandler<GetAdminPaymentQuery, Result<PaymentAdminResponse>> _getAdminPaymentHandler;
    private readonly IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> _getAdminPaymentStatsHandler;

    public AdminPaymentsController(
        IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> getAdminPaymentsHandler,
        IQueryHandler<GetAdminPaymentQuery, Result<PaymentAdminResponse>> getAdminPaymentHandler,
        IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> getAdminPaymentStatsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _getAdminPaymentsHandler = getAdminPaymentsHandler;
        _getAdminPaymentHandler = getAdminPaymentHandler;
        _getAdminPaymentStatsHandler = getAdminPaymentStatsHandler;
    }

    /// <summary>
    /// Lists all Payments across customers, newest first, with optional status, order,
    /// customer and creation-date filters. Pagination falls back to page 1 with a page
    /// size of 20 (maximum 100).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PaymentAdminResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAdminPayments(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] Guid? orderId,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var result = await _getAdminPaymentsHandler.HandleAsync(
            new GetAdminPaymentsQuery(pageNumber, pageSize, status, orderId, customerId, from, to),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Returns aggregate Payment counts grouped by status plus succeeded totals per
    /// currency for the administrative overview.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<PaymentAdminStatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminPaymentStats(CancellationToken cancellationToken)
    {
        var result = await _getAdminPaymentStatsHandler.HandleAsync(
            new GetAdminPaymentStatsQuery(),
            cancellationToken);

        return Success(result);
    }

    /// <summary>
    /// Retrieves a single Payment by identity without customer ownership scoping.
    /// </summary>
    [HttpGet("{paymentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentAdminResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdminPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        var result = await _getAdminPaymentHandler.HandleAsync(
            new GetAdminPaymentQuery(paymentId),
            cancellationToken);

        return Success(result);
    }
}
