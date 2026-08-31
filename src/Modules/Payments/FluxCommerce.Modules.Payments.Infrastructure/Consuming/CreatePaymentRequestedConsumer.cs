using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Consuming;

/// <summary>
/// Phase 10.3: Payments consumer for <c>payments.create_payment_requested</c> (saga-to-payment initiation).
/// The Order Saga stages this event after <c>inventory.reserved â†’ WaitingForPayment</c> via the Orders outbox.
/// Payments owns intent creation and Stripe interaction; this consumer executes the payment creation
/// idempotently inside the Payments boundary (no cross-DbContext, no direct Stripe call from Orders).
/// Duplicate deliveries for the same <c>OrderId</c> are safe: existing Payment with Provider intent is
/// returned without a second Stripe call; unique constraint <c>UX_Payments_OrderId</c> guards the
/// concurrent race; Stripe idempotency key (<c>intent_{OrderId}_{PaymentId}_{Version}</c>) guards
/// provider-level duplicates. The consumer is transport-agnostic and runs inside the Payments
/// idempotent-processing boundary when wired with <c>PaymentsIdempotentProcessor</c> or, without it,
/// via application-level idempotency checks.
/// </summary>
public sealed class CreatePaymentRequestedConsumer : IIntegrationEventHandler<CreatePaymentRequestedIntegrationEvent>
{
    public const string StableHandlerName = "Payments.CreatePaymentFromSaga";

    private static readonly ActivitySource ActivitySource = new("FluxCommerce");

    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripePaymentGateway _stripeGateway;
    private readonly ILogger<CreatePaymentRequestedConsumer> _logger;

    public CreatePaymentRequestedConsumer(
        IPaymentRepository paymentRepository,
        IStripePaymentGateway stripeGateway,
        ILogger<CreatePaymentRequestedConsumer> logger)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _stripeGateway = stripeGateway ?? throw new ArgumentNullException(nameof(stripeGateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        CreatePaymentRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        return HandleCoreAsync(@event, envelope, cancellationToken);
    }

    private async Task HandleCoreAsync(
        CreatePaymentRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Payments.HandleCreatePaymentRequested", ActivityKind.Internal);
        activity?.SetTag("order.id", @event.OrderId);
        activity?.SetTag("customer.id", @event.CustomerId);
        activity?.SetTag("payment.amount", @event.Amount);
        activity?.SetTag("payment.currency", @event.Currency);
        activity?.SetTag("correlation.id", envelope.CorrelationId);
        activity?.SetTag("causation.id", envelope.CausationId);

        Validate(@event);

        var existing = await _paymentRepository.GetByOrderIdAsync(@event.OrderId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(existing.ProviderPaymentIntentId))
            {
                _logger.LogInformation("Payment already exists for order {OrderId} with intent {IntentId}; duplicate create_payment_requested acknowledged (correlation {CorrelationId}).",
                    @event.OrderId, existing.ProviderPaymentIntentId, envelope.CorrelationId);
                activity?.SetTag("payment.id", existing.Id);
                activity?.SetTag("payment.duplicate", true);
                return;
            }

            // Existing pending without intent: reuse it (idempotent retry)
            var reuseRequest = new CreatePaymentIntentRequest(
                existing.OrderId, existing.Id, existing.Amount, existing.Currency, existing.Version);

            try
            {
                var reused = await _stripeGateway.CreatePaymentIntentAsync(reuseRequest, cancellationToken).ConfigureAwait(false);
                existing.UpdateProviderReference(reused.StripePaymentIntentId, "Stripe");
                if (existing.Status == Domain.Enums.PaymentStatus.Pending)
                {
                    try { existing.Process(); } catch { }
                }

                await _paymentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Reused existing payment {PaymentId} for order {OrderId} with new intent {IntentId} (correlation {CorrelationId}).",
                    existing.Id, @event.OrderId, reused.StripePaymentIntentId, envelope.CorrelationId);
                activity?.SetTag("payment.id", existing.Id);
                activity?.SetTag("stripe.payment_intent_id", reused.StripePaymentIntentId);
                return;
            }
            catch (Exception ex) when (IsStripeException(ex))
            {
                _logger.LogError(ex, "Stripe failed to create intent for existing payment {PaymentId} order {OrderId} (correlation {CorrelationId}).",
                    existing.Id, @event.OrderId, envelope.CorrelationId);
                throw;
            }
        }

        // No existing payment: create new
        var payment = new Payment(Guid.NewGuid(), @event.OrderId, @event.Amount, @event.Currency, @event.CustomerId);
        activity?.SetTag("payment.id", payment.Id);

        try
        {
            await _paymentRepository.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex, "UX_Payments_OrderId"))
        {
            var winner = await _paymentRepository.GetByOrderIdAsync(@event.OrderId, cancellationToken).ConfigureAwait(false);
            if (winner is not null && !string.IsNullOrWhiteSpace(winner.ProviderPaymentIntentId))
            {
                _logger.LogInformation("Concurrent duplicate payment creation for order {OrderId} detected; winner {PaymentId} already has intent (correlation {CorrelationId}).",
                    @event.OrderId, winner.Id, envelope.CorrelationId);
                return;
            }

            _logger.LogWarning(ex, "Concurrent duplicate payment creation for order {OrderId} without intent; will retry via redelivery (correlation {CorrelationId}).",
                @event.OrderId, envelope.CorrelationId);
            throw new InvalidOperationException($"Concurrent payment creation for order '{@event.OrderId}': {ex.Message}", ex);
        }

        var request = new CreatePaymentIntentRequest(payment.OrderId, payment.Id, payment.Amount, payment.Currency, payment.Version);
        PaymentIntentResult stripeResult;
        try
        {
            stripeResult = await _stripeGateway.CreatePaymentIntentAsync(request, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("stripe.payment_intent_id", stripeResult.StripePaymentIntentId);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex) when (IsStripeException(ex))
        {
            _logger.LogError(ex, "Stripe failed to create intent for payment {PaymentId} order {OrderId} (correlation {CorrelationId}).",
                payment.Id, @event.OrderId, envelope.CorrelationId);
            throw;
        }

        payment.UpdateProviderReference(stripeResult.StripePaymentIntentId, "Stripe");
        if (payment.Status == Domain.Enums.PaymentStatus.Pending)
        {
            try { payment.Process(); } catch { }
        }

        try
        {
            await _paymentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created payment {PaymentId} for order {OrderId} with intent {IntentId} (correlation {CorrelationId}).",
                payment.Id, @event.OrderId, stripeResult.StripePaymentIntentId, envelope.CorrelationId);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex, "UX_Payments_ProviderPaymentIntentId"))
        {
            _logger.LogWarning(ex, "Duplicate provider intent for payment {PaymentId} order {OrderId} (correlation {CorrelationId}).",
                payment.Id, @event.OrderId, envelope.CorrelationId);
            throw new InvalidOperationException($"Duplicate provider intent for order '{@event.OrderId}': {ex.Message}", ex);
        }
    }

    private static void Validate(CreatePaymentRequestedIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.CustomerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(@event));
        if (@event.Amount <= 0)
            throw new ArgumentException("Amount must be positive.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.Currency))
            throw new ArgumentException("Currency is required.", nameof(@event));
    }

    private static bool IsStripeException(Exception ex) =>
        ex.GetType().Name.Contains("Stripe", StringComparison.OrdinalIgnoreCase) ||
        (ex.Message?.Contains("Stripe", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsUniqueConstraintViolation(Exception ex, string constraintName)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains(constraintName, StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}

