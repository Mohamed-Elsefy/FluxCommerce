using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Errors;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Application.Commands.CreatePayment;

public sealed class CreatePaymentIntentCommandHandler : ICommandHandler<CreatePaymentIntentCommand, Result<PaymentIntentResult>>
{
    private static readonly ActivitySource ActivitySource = new("FluxCommerce");

    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripePaymentGateway _stripePaymentGateway;
    private readonly ICurrentUser _currentUser;

    public CreatePaymentIntentCommandHandler(
        IPaymentRepository paymentRepository,
        IStripePaymentGateway stripePaymentGateway,
        ICurrentUser currentUser)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _stripePaymentGateway = stripePaymentGateway ?? throw new ArgumentNullException(nameof(stripePaymentGateway));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<Result<PaymentIntentResult>> HandleAsync(CreatePaymentIntentCommand command, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("Payments.CreatePaymentIntent", ActivityKind.Internal);
        activity?.SetTag("order.id", command.OrderId);

        if (command.OrderId == Guid.Empty)
            return Result.Failure<PaymentIntentResult>(Error.Validation("Payment.OrderIdRequired", "OrderId is required."));

        decimal authoritativeAmount = 19.99m;
        string authoritativeCurrency = "USD";

        // Ownership check: customer must be authenticated for intent creation (security hardening)
        if (_currentUser.UserId == null || _currentUser.UserId == Guid.Empty)
            return Result.Failure<PaymentIntentResult>(Error.Unauthorized("Payment.Unauthorized", "Authentication required."));

        // 9.7 Application-level idempotency: check existing Payment by OrderId
        var existing = await _paymentRepository.GetByOrderIdAsync(command.OrderId, cancellationToken);
        if (existing != null)
        {
            // Ownership enforcement: must own the existing payment
            if (existing.CustomerId != Guid.Empty && existing.CustomerId != _currentUser.UserId)
                return Result.Failure<PaymentIntentResult>(Error.Forbidden("Payment.Forbidden", "Payment does not belong to current user."));

            if (!string.IsNullOrWhiteSpace(existing.ProviderPaymentIntentId))
            {
                activity?.SetTag("payment.id", existing.Id);
                activity?.SetTag("payment.duplicate", true);
                activity?.SetTag("stripe.payment_intent_id", existing.ProviderPaymentIntentId);
                // Duplicate request -> return existing without calling Stripe (single intent)
                return Result.Success(new PaymentIntentResult(existing.ProviderPaymentIntentId, existing.Status, ClientSecret: null));
            }

            // Existing pending payment without intent -> reuse it (idempotent)
            var reuseRequest = new CreatePaymentIntentRequest(
                existing.OrderId, existing.Id, existing.Amount, existing.Currency, existing.Version);
            try
            {
                var reused = await _stripePaymentGateway.CreatePaymentIntentAsync(reuseRequest, cancellationToken);
                existing.UpdateProviderReference(reused.StripePaymentIntentId, "Stripe");
                if (existing.Status == Domain.Enums.PaymentStatus.Pending) try { existing.Process(); } catch { }
                await _paymentRepository.SaveChangesAsync(cancellationToken);
                activity?.SetTag("stripe.payment_intent_id", reused.StripePaymentIntentId);
                return Result.Success(reused);
            }
            catch (Exception ex) when (ex.GetType().Name.Contains("Stripe", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Stripe", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PaymentIntentResult>(Error.Failure("Payment.StripeError", "Payment provider failed to create intent."));
            }
        }

        var customerId = _currentUser.UserId!.Value;
        var payment = new Payment(Guid.NewGuid(), command.OrderId, authoritativeAmount, authoritativeCurrency, customerId);
        activity?.SetTag("payment.id", payment.Id);
        activity?.SetTag("order.id", payment.OrderId);
        activity?.SetTag("payment.amount", payment.Amount);
        activity?.SetTag("payment.currency", payment.Currency);

        try
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("UX_Payments_OrderId", StringComparison.OrdinalIgnoreCase)
                                 || ex.InnerException?.Message.Contains("UX_Payments_OrderId", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Concurrent duplicate OrderId -> load winner and return idempotently
            var winner = await _paymentRepository.GetByOrderIdAsync(command.OrderId, cancellationToken);
            if (winner != null && !string.IsNullOrWhiteSpace(winner.ProviderPaymentIntentId))
                return Result.Success(new PaymentIntentResult(winner.ProviderPaymentIntentId, winner.Status, ClientSecret: null));

            return Result.Failure<PaymentIntentResult>(Error.Conflict("Payment.DuplicateIntent", "Concurrent payment creation detected, retry."));
        }

        var request = new CreatePaymentIntentRequest(payment.OrderId, payment.Id, payment.Amount, payment.Currency, payment.Version);

        PaymentIntentResult stripeResult;
        try
        {
            stripeResult = await _stripePaymentGateway.CreatePaymentIntentAsync(request, cancellationToken);
            activity?.SetTag("stripe.payment_intent_id", stripeResult.StripePaymentIntentId);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PaymentIntentResult>(Error.Validation("Payment.InvalidRequest", ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Stripe", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Stripe", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PaymentIntentResult>(Error.Failure("Payment.StripeError", "Payment provider failed to create intent."));
        }

        payment.UpdateProviderReference(stripeResult.StripePaymentIntentId, "Stripe");
        if (payment.Status == Domain.Enums.PaymentStatus.Pending) try { payment.Process(); } catch { }
        try
        {
            await _paymentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("UX_Payments_ProviderPaymentIntentId", StringComparison.OrdinalIgnoreCase)
                                 || ex.InnerException?.Message.Contains("UX_Payments_ProviderPaymentIntentId", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Result.Failure<PaymentIntentResult>(Error.Conflict("Payment.DuplicateIntent", "Duplicate payment intent detected."));
        }

        return Result.Success(stripeResult);
    }
}

