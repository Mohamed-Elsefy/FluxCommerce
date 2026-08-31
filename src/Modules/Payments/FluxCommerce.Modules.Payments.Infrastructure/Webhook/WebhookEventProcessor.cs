using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Domain.Entities;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Webhook;

public sealed class WebhookEventProcessor : IWebhookEventProcessor
{
    private static readonly HashSet<string> SupportedEvents = new(StringComparer.Ordinal)
    {
        "payment_intent.succeeded",
        "payment_intent.payment_failed",
        "payment_intent.canceled",
        "payment_intent.processing"
    };

    private readonly PaymentsDbContext _dbContext;
    private readonly IntegrationEventOutboxMessageFactory _outboxFactory;
    private readonly ILogger<WebhookEventProcessor> _logger;

    public WebhookEventProcessor(PaymentsDbContext dbContext, IntegrationEventOutboxMessageFactory outboxFactory, ILogger<WebhookEventProcessor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _outboxFactory = outboxFactory ?? throw new ArgumentNullException(nameof(outboxFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, CancellationToken cancellationToken = default)
        => TryProcessAsync(stripeEventId, eventType, paymentIntentId: null, cancellationToken);

    public async Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentsActivitySource.Source.StartActivity("Payments.Webhook.Process", ActivityKind.Internal);
        activity?.SetTag("stripe.event_id", stripeEventId);
        activity?.SetTag("stripe.event_type", eventType);
        activity?.SetTag("stripe.payment_intent_id", paymentIntentId);

        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("StripeEventId is required.", nameof(stripeEventId));

        var alreadyExists = await _dbContext.Set<ProcessedWebhookEvent>()
            .AsNoTracking()
            .AnyAsync(e => e.StripeEventId == stripeEventId, cancellationToken);

        if (alreadyExists)
        {
            _logger.LogInformation("Duplicate webhook event detected before insert. StripeEventId={StripeEventId}, EventType={EventType}", stripeEventId, eventType);
            return WebhookProcessResult.AlreadyProcessed;
        }

        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment? payment = null;
        PaymentStatus statusBefore = default;
        bool stateChanged = false;
        string? failureReason = null;

        if (!string.IsNullOrWhiteSpace(eventType) && SupportedEvents.Contains(eventType))
        {
            if (string.IsNullOrWhiteSpace(paymentIntentId))
            {
                _logger.LogWarning("Supported webhook {EventType} arrived without PaymentIntentId. StripeEventId={StripeEventId}", eventType, stripeEventId);
            }
            else
            {
                payment = await _dbContext.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>()
                    .FirstOrDefaultAsync(p => p.ProviderPaymentIntentId == paymentIntentId, cancellationToken);

                if (payment is null)
                {
                    _logger.LogWarning("Payment not found for webhook. StripeEventId={StripeEventId}, EventType={EventType}, PaymentIntentId={PaymentIntentId}", stripeEventId, eventType, paymentIntentId);
                }
                else
                {
                    statusBefore = payment.Status;
                    try
                    {
                        stateChanged = ApplyEventToPayment(payment, eventType, paymentIntentId, out failureReason);
                        if (stateChanged)
                            _logger.LogInformation("Payment state transition applied via webhook. StripeEventId={StripeEventId}, EventType={EventType}, PaymentId={PaymentId}, {OldStatus}->{NewStatus}", stripeEventId, eventType, payment.Id, statusBefore, payment.Status);
                        else
                            _logger.LogInformation("Payment webhook event received but no state change required. StripeEventId={StripeEventId}, EventType={EventType}, PaymentId={PaymentId}", stripeEventId, eventType, payment.Id);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "Out-of-order or invalid transition ignored. StripeEventId={StripeEventId}, EventType={EventType}, PaymentId={PaymentId}, CurrentStatus={Status}", stripeEventId, eventType, payment.Id, payment.Status);
                    }
                }
            }
        }
        else
        {
            _logger.LogInformation("Unsupported webhook event type received, no state change. StripeEventId={StripeEventId}, EventType={EventType}", stripeEventId, eventType);
        }

        var processedEvent = new ProcessedWebhookEvent(Guid.NewGuid(), stripeEventId, eventType, DateTimeOffset.UtcNow);
        _dbContext.Set<ProcessedWebhookEvent>().Add(processedEvent);

        // 9.11: map domain state change â†’ integration event + Outbox in same transaction
        if (payment != null && stateChanged)
        {
            OutboxMessage? outbox = null;
            var now = DateTimeOffset.UtcNow;

            if (payment.Status == PaymentStatus.Succeeded && eventType == "payment_intent.succeeded")
            {
                var evt = new PaymentSucceededIntegrationEvent(
                    payment.OrderId,
                    payment.Id,
                    payment.ProviderPaymentIntentId ?? paymentIntentId ?? string.Empty,
                    payment.Amount,
                    payment.Currency,
                    payment.Status.ToString(),
                    now)
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = payment.OrderId,
                    CausationId = processedEvent.Id,
                    OccurredAtUtc = now.UtcDateTime,
                    Version = FluxCommerce.BuildingBlocks.Messaging.Versioning.EventVersion.V1
                };
                outbox = _outboxFactory.Create(PaymentSucceededIntegrationEvent.EventType, evt);
            }
            else if (payment.Status == PaymentStatus.Failed && eventType == "payment_intent.payment_failed")
            {
                var evt = new PaymentFailedIntegrationEvent(
                    payment.OrderId,
                    payment.Id,
                    payment.ProviderPaymentIntentId ?? paymentIntentId ?? string.Empty,
                    failureReason ?? "Stripe reported payment_failed",
                    payment.Status.ToString(),
                    now)
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = payment.OrderId,
                    CausationId = processedEvent.Id,
                    OccurredAtUtc = now.UtcDateTime,
                    Version = FluxCommerce.BuildingBlocks.Messaging.Versioning.EventVersion.V1
                };
                outbox = _outboxFactory.Create(PaymentFailedIntegrationEvent.EventType, evt);
            }

            if (outbox != null)
            {
                _dbContext.Set<OutboxMessage>().Add(outbox);
                _logger.LogInformation("Staged integration event {EventType} for Outbox. StripeEventId={StripeEventId}, PaymentId={PaymentId}, CorrelationId={CorrelationId}, CausationId={CausationId}", outbox.Type, stripeEventId, payment.Id, outbox.CorrelationId, outbox.CausationId);
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return WebhookProcessResult.Processed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(ex, "Concurrent duplicate webhook race handled via unique constraint. StripeEventId={StripeEventId}", stripeEventId);
            _dbContext.Entry(processedEvent).State = EntityState.Detached;
            return WebhookProcessResult.AlreadyProcessed;
        }
    }

    private static bool ApplyEventToPayment(FluxCommerce.Modules.Payments.Domain.Aggregates.Payment payment, string eventType, string paymentIntentId, out string? failureReason)
    {
        failureReason = null;
        var before = payment.Status;
        switch (eventType)
        {
            case "payment_intent.succeeded":
                payment.Succeed(paymentIntentId);
                return payment.Status == PaymentStatus.Succeeded && before != PaymentStatus.Succeeded;
            case "payment_intent.payment_failed":
                failureReason = "Stripe payment_failed";
                payment.Fail(failureReason);
                return payment.Status == PaymentStatus.Failed && before != PaymentStatus.Failed;
            case "payment_intent.canceled":
                payment.Cancel();
                return payment.Status == PaymentStatus.Cancelled && before != PaymentStatus.Cancelled;
            case "payment_intent.processing":
                payment.Process();
                return payment.Status == PaymentStatus.Processing && before != PaymentStatus.Processing;
            default:
                return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UX_ProcessedWebhookEvents_StripeEventId", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}

