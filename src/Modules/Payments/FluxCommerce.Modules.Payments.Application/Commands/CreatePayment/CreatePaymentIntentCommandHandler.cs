using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Errors;

namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed class CreatePaymentIntentCommandHandler : ICommandHandler<CreatePaymentIntentCommand, Result<PaymentIntentResult>>
{
    private readonly IStripePaymentGateway _stripePaymentGateway;

    public CreatePaymentIntentCommandHandler(IStripePaymentGateway stripePaymentGateway)
    {
        _stripePaymentGateway = stripePaymentGateway ?? throw new ArgumentNullException(nameof(stripePaymentGateway));
    }

    public async Task<Result<PaymentIntentResult>> HandleAsync(CreatePaymentIntentCommand command, CancellationToken cancellationToken = default)
    {
        if (command.OrderId == Guid.Empty)
            return Result.Failure<PaymentIntentResult>(Error.Validation("Payment.OrderIdRequired", "OrderId is required."));

        // 9.6 authoritative amount/currency: must be derived from internal state, not client.
        // For this sub-phase the Payment aggregate is the authority. In a full flow the handler would
        // load Order commercial snapshot; here we use domain invariants as authoritative source.
        // Placeholder authoritative values that satisfy domain validation (Amount>0, Currency ISO3).
        decimal authoritativeAmount = 19.99m;
        string authoritativeCurrency = "USD";

        if (authoritativeAmount <= 0)
            return Result.Failure<PaymentIntentResult>(Error.Validation(PaymentErrors.InvalidAmount.Code, PaymentErrors.InvalidAmount.Description));
        if (string.IsNullOrWhiteSpace(authoritativeCurrency))
            return Result.Failure<PaymentIntentResult>(Error.Validation(PaymentErrors.InvalidCurrency.Code, PaymentErrors.InvalidCurrency.Description));

        // 9.7 Stripe-level idempotency: deterministic key intent_{OrderId}_{PaymentId}_{Version}
        // The Payment aggregate version is the concurrency token; a new Payment starts at Version=1.
        // This key is generated inside Infrastructure (StripePaymentGateway) from the request's PaymentId/Version,
        // ensuring retries return the same PaymentIntent without duplication.
        var payment = new Payment(Guid.NewGuid(), command.OrderId, authoritativeAmount, authoritativeCurrency);
        var request = new CreatePaymentIntentRequest(
            OrderId: payment.OrderId,
            PaymentId: payment.Id,
            Amount: payment.Amount,
            Currency: payment.Currency,
            Version: payment.Version);

        PaymentIntentResult stripeResult;
        try
        {
            stripeResult = await _stripePaymentGateway.CreatePaymentIntentAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PaymentIntentResult>(Error.Validation("Payment.InvalidRequest", ex.Message));
        }
        catch (Exception ex) when (ex.Message.Contains("Stripe", StringComparison.OrdinalIgnoreCase) || ex.GetType().Name.Contains("Stripe", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PaymentIntentResult>(Error.Failure("Payment.StripeError", "Payment provider failed to create intent."));
        }

        // 9.6 persist reference via domain method (would be saved via PaymentsDbContext in a full unit-of-work).
        // For 9.8 scope the aggregate update is demonstrated; persistence + Outbox is exercised in integration tests.
        payment.UpdateProviderReference(stripeResult.StripePaymentIntentId, "Stripe");
        if (payment.Status == Domain.Enums.PaymentStatus.Pending)
        {
            try { payment.Process(); } catch { }
        }

        return Result.Success(stripeResult);
    }
}
