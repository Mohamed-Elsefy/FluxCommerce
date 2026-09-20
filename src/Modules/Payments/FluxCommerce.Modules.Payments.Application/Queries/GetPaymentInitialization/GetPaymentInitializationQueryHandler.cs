using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Repositories;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;

/// <summary>
/// Resolves the Stripe client secret for the owner of an order.
/// The saga owns intent creation; this query is read-only and never creates a Stripe intent.
/// A live secret is re-fetched from the provider on every call, so retries reuse the same
/// single intent per order (<c>intent_{OrderId}_{PaymentId}_{Version}</c>) instead of minting new ones.
/// </summary>
public sealed class GetPaymentInitializationQueryHandler
    : IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>>
{
    private static readonly ActivitySource ActivitySource = new("FluxCommerce");

    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripePaymentGateway _stripePaymentGateway;
    private readonly ICurrentUser _currentUser;

    public GetPaymentInitializationQueryHandler(
        IPaymentRepository paymentRepository,
        IStripePaymentGateway stripePaymentGateway,
        ICurrentUser currentUser)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _stripePaymentGateway = stripePaymentGateway ?? throw new ArgumentNullException(nameof(stripePaymentGateway));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<PaymentInitializationResponse>> HandleAsync(
        GetPaymentInitializationQuery query,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("Payments.GetPaymentInitialization", ActivityKind.Internal);
        activity?.SetTag("order.id", query.OrderId);

        if (query.OrderId == Guid.Empty)
            return Result.Failure<PaymentInitializationResponse>(
                Error.Validation("Payment.OrderIdRequired", "OrderId is required."));

        if (_currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result.Failure<PaymentInitializationResponse>(
                Error.Unauthorized("Payment.Unauthorized", "Authentication required."));

        var payment = await _paymentRepository.GetByOrderIdAsync(query.OrderId, cancellationToken).ConfigureAwait(false);

        // Ownership enforcement mirrors PaymentsController.GetPayment: strangers (and missing payments)
        // get the same masked 404 so order/payment existence is never leaked.
        if (payment is null)
            return Result.Failure<PaymentInitializationResponse>(
                Error.NotFound("Payment.NotFound", "Payment not found."));

        if (payment.CustomerId != Guid.Empty && payment.CustomerId != userId)
            return Result.Failure<PaymentInitializationResponse>(
                Error.NotFound("Payment.NotFound", "Payment not found."));

        activity?.SetTag("payment.id", payment.Id);

        if (!payment.CanBePaid())
            return Result.Failure<PaymentInitializationResponse>(
                Error.Conflict("Payment.NotPayable", "Payment can no longer be initialized."));

        if (string.IsNullOrWhiteSpace(payment.ProviderPaymentIntentId))
            return Result.Failure<PaymentInitializationResponse>(
                Error.Conflict("Payment.NotReady", "Payment is not ready yet; retry shortly."));

        string? clientSecret;
        try
        {
            clientSecret = await _stripePaymentGateway
                .RetrieveClientSecretAsync(payment.ProviderPaymentIntentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PaymentInitializationResponse>(
                Error.Validation("Payment.InvalidRequest", ex.Message));
        }
        catch (Exception)
        {
            return Result.Failure<PaymentInitializationResponse>(
                Error.Failure("Payment.ProviderUnavailable", "Payment provider is temporarily unavailable."));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
            return Result.Failure<PaymentInitializationResponse>(
                Error.Failure("Payment.ProviderUnavailable", "Payment provider is temporarily unavailable."));

        return Result.Success(new PaymentInitializationResponse(
            payment.Id,
            payment.OrderId,
            clientSecret,
            payment.ProviderPaymentIntentId,
            payment.Status.ToString()));
    }
}
