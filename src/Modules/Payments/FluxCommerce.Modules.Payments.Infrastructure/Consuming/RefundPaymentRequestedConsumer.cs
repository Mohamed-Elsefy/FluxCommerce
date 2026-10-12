using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Consuming;

/// <summary>
/// Phase 10.8: handles refund requests idempotently. Uses PaymentsIdempotentProcessor at the
/// infrastructure boundary; handler itself is also idempotent via local Payment status check
/// and Stripe idempotency key (refund_{PaymentId}_{OrderId}).
/// </summary>
public sealed class RefundPaymentRequestedConsumer : IIntegrationEventHandler<RefundPaymentRequestedIntegrationEvent>
{
    public const string StableHandlerName = "Payments.RefundPayment";

    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentGateway _stripeGateway;
    private readonly PaymentsDbContext _dbContext;
    private readonly IntegrationEventOutboxMessageFactory _outboxFactory;
    private readonly ILogger<RefundPaymentRequestedConsumer> _logger;

    public RefundPaymentRequestedConsumer(
        IPaymentRepository paymentRepository,
        IPaymentGateway stripeGateway,
        PaymentsDbContext dbContext,
        IntegrationEventOutboxMessageFactory outboxFactory,
        ILogger<RefundPaymentRequestedConsumer> logger)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _stripeGateway = stripeGateway ?? throw new ArgumentNullException(nameof(stripeGateway));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _outboxFactory = outboxFactory ?? throw new ArgumentNullException(nameof(outboxFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(RefundPaymentRequestedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(@event);
        return HandleCoreAsync(@event, envelope, cancellationToken);
    }

    private async Task HandleCoreAsync(RefundPaymentRequestedIntegrationEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("FluxCommerce").StartActivity("Payments.HandleRefundRequested", ActivityKind.Internal);
        activity?.SetTag("order.id", @event.OrderId);
        activity?.SetTag("payment.id", @event.PaymentId);
        activity?.SetTag("correlation.id", envelope.CorrelationId);

        var payment = await _paymentRepository.GetByIdAsync(@event.PaymentId, cancellationToken).ConfigureAwait(false)
                      ?? await _paymentRepository.GetByOrderIdAsync(@event.OrderId, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            _logger.LogWarning("Payment not found for refund request OrderId {OrderId} PaymentId {PaymentId}", @event.OrderId, @event.PaymentId);
            throw new InvalidOperationException($"Payment not found for Order {@event.OrderId}");
        }

        if (payment.Status == Domain.Enums.PaymentStatus.Refunded)
        {
            _logger.LogInformation("Payment {PaymentId} already refunded locally; duplicate refund request acknowledged (correlation {CorrelationId})", payment.Id, envelope.CorrelationId);
            return;
        }

        if (payment.Status != Domain.Enums.PaymentStatus.Succeeded)
        {
            _logger.LogWarning("Payment {PaymentId} status {Status} is not Succeeded; cannot refund. Request will be retried or dead-lettered.", payment.Id, payment.Status);
            throw new InvalidOperationException($"Payment {payment.Id} status {payment.Status} cannot be refunded");
        }

        // Ensure provider intent id is available
        var providerIntentId = !string.IsNullOrWhiteSpace(payment.ProviderPaymentIntentId)
            ? payment.ProviderPaymentIntentId!
            : @event.ProviderPaymentIntentId;

        if (string.IsNullOrWhiteSpace(providerIntentId))
        {
            _logger.LogWarning("ProviderPaymentIntentId missing for payment {PaymentId}; cannot refund", payment.Id);
            throw new InvalidOperationException($"ProviderPaymentIntentId missing for payment {payment.Id}");
        }

        string? stripeRefundId = null;
        try
        {
            var request = new CreateRefundRequest(payment.OrderId, payment.Id, providerIntentId, payment.Amount, payment.Currency);
            var result = await _stripeGateway.CreateRefundAsync(request, cancellationToken).ConfigureAwait(false);
            stripeRefundId = result.StripeRefundId;
            activity?.SetTag("stripe.refund_id", stripeRefundId);
            _logger.LogInformation("Stripe refund created {RefundId} for payment {PaymentId} order {OrderId}", stripeRefundId, payment.Id, payment.OrderId);
        }
        catch (Exception ex) when (IsAlreadyRefundedException(ex))
        {
            _logger.LogInformation(ex, "Stripe reports payment {PaymentId} already refunded; treating as success (idempotent)", payment.Id);
            // Treat as success, continue to local refund
        }
        catch (Exception ex) when (IsTransientException(ex))
        {
            _logger.LogWarning(ex, "Transient Stripe error for refund payment {PaymentId}; will retry", payment.Id);
            throw; // retry via infrastructure
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Permanent Stripe refund failure for payment {PaymentId}", payment.Id);
            throw; // will be retried then dead-lettered; local state not corrupted
        }

        try
        {
            payment.Refund(stripeRefundId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("AlreadyRefunded"))
        {
            _logger.LogInformation("Payment {PaymentId} already refunded locally after Stripe call; duplicate acknowledged", payment.Id);
            return;
        }

        var refundedEvent = new PaymentRefundedIntegrationEvent(
            payment.OrderId,
            payment.Id,
            providerIntentId,
            stripeRefundId,
            payment.Amount,
            payment.Currency,
            DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = envelope.ProducedEventCorrelationId,
            CausationId = envelope.ProducedEventCausationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        var outbox = _outboxFactory.Create(PaymentRefundedIntegrationEvent.EventType, refundedEvent);
        _dbContext.OutboxMessages.Add(outbox);
        // Atomic: Payment status + outbox in same SaveChanges (within idempotent processor transaction)
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Staged PaymentRefunded for order {OrderId} payment {PaymentId} (correlation {CorrelationId})", payment.OrderId, payment.Id, outbox.CorrelationId);
    }

    private static void Validate(RefundPaymentRequestedIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty) throw new ArgumentException("OrderId is required.", nameof(@event));
        if (@event.PaymentId == Guid.Empty) throw new ArgumentException("PaymentId is required.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.ProviderPaymentIntentId)) throw new ArgumentException("ProviderPaymentIntentId is required.", nameof(@event));
        if (@event.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(@event));
        if (string.IsNullOrWhiteSpace(@event.Currency)) throw new ArgumentException("Currency is required.", nameof(@event));
    }

    private static bool IsAlreadyRefundedException(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("already refunded", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("already_refunded", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("refund already", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientException(Exception ex)
    {
        // Simple heuristic: network/timeout/5xx are transient
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("transient", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               ex is HttpRequestException ||
               ex is TaskCanceledException;
    }
}

