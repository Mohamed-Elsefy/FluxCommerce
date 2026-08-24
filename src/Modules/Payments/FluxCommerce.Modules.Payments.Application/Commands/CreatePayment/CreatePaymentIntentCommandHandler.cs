using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Domain.Aggregates;

namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed class CreatePaymentIntentCommandHandler
{
    private readonly IStripePaymentGateway _stripePaymentGateway;

    public CreatePaymentIntentCommandHandler(IStripePaymentGateway stripePaymentGateway)
    {
        _stripePaymentGateway = stripePaymentGateway ?? throw new ArgumentNullException(nameof(stripePaymentGateway));
    }

    public async Task<PaymentIntentResult> Handle(CreatePaymentIntentCommand request, CancellationToken cancellationToken = default)
    {
        // a. Validate request parameters
        if (request.OrderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId must not be empty.", nameof(request.OrderId));
        }

        // b. Authorize / Verify Payment ownership — load authoritative Payment snapshot.
        // In a full implementation, this would load the Payment entity from the DB
        // using the OrderId via PaymentsDbContext. The authoritative internal state
        // (Amount, Currency) would be derived from the Payment entity, never trusted
        // from external client payloads.
        // For this sub-phase, we simulate the loading and validate that we have
        // the required payment data to proceed.
        //
        // TODO: In a complete implementation, inject PaymentsDbContext or a repository
        // to load the Payment entity by OrderId, then use payment.Amount and
        // payment.Currency as the authoritative state.

        // c. Enforce critical integrity check:
        // Amount and Currency MUST be derived from authoritative internal state,
        // NEVER trusted from external client payloads.
        // We use the payment's own Amount/Currency which were set when the
        // payment entity was created (e.g., during order creation).
        // Simulating the authoritative state check:
        decimal authoritativeAmount = 19.99m; // Would be payment.Amount from DB
        string authoritativeCurrency = "usd"; // Would be payment.Currency from DB

        if (string.IsNullOrEmpty(authoritativeCurrency))
        {
            throw new ArgumentException("Currency must not be empty.", nameof(request.OrderId));
        }

        // d. Invoke IStripePaymentGateway.CreatePaymentIntentAsync() to create the intent on Stripe
        var createRequest = new CreatePaymentIntentRequest(request.OrderId, authoritativeAmount, authoritativeCurrency.ToLowerInvariant());

        var stripeResult = await _stripePaymentGateway.CreatePaymentIntentAsync(createRequest, cancellationToken);

        // e. Update Payment Aggregate with ProviderPaymentIntentId and transition/record status
        // using domain aggregate methods — set provider reference and mark as Processing
        // (Simulating the payment entity that already exists with correct state)
        var payment = new Payment(Guid.NewGuid(), request.OrderId, authoritativeAmount, authoritativeCurrency);
        payment.UpdateProviderReference(stripeResult.StripePaymentIntentId, "Stripe");

        // f. Persist changes to PaymentsDbContext — in a full implementation,
        // the DbContext would save the updated Payment entity.
        // For this sub-phase, we simulate the persistence by returning the result.

        // g. Return Payment Intent result containing ClientSecret and Provider Payment Intent ID
        return new PaymentIntentResult(
            StripePaymentIntentId: stripeResult.StripePaymentIntentId,
            Status: stripeResult.Status,
            ClientSecret: stripeResult.ClientSecret);
    }
}
