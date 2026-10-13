using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Services;
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
    private readonly PaymentsDbContext _dbContext;
    private readonly IntegrationEventOutboxMessageFactory _outboxFactory;
    private readonly IPaymentWebhookInterpreter _interpreter;
    private readonly ILogger<WebhookEventProcessor> _logger;

    public WebhookEventProcessor(
        PaymentsDbContext dbContext,
        IntegrationEventOutboxMessageFactory outboxFactory,
        IPaymentWebhookInterpreter interpreter,
        ILogger<WebhookEventProcessor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _outboxFactory = outboxFactory ?? throw new ArgumentNullException(nameof(outboxFactory));
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, CancellationToken cancellationToken = default)
        => TryProcessAsync(stripeEventId, eventType, paymentIntentId: null, cancellationToken: cancellationToken);

    public Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, CancellationToken cancellationToken)
        => TryProcessAsync(stripeEventId, eventType, paymentIntentId, amountMinor: null, currency: null, stripeFailureReason: null, cancellationToken: cancellationToken);

    public async Task<WebhookProcessResult> TryProcessAsync(string stripeEventId, string? eventType, string? paymentIntentId, long? amountMinor = null, string? currency = null, string? stripeFailureReason = null, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentsActivitySource.Source.StartActivity("Payments.Webhook.Process", ActivityKind.Internal);
        activity?.SetTag("stripe.event_id", stripeEventId);
        activity?.SetTag("stripe.event_type", eventType);
        activity?.SetTag("stripe.payment_intent_id", paymentIntentId);
        activity?.SetTag("stripe.amount_minor", amountMinor);
        activity?.SetTag("stripe.currency", currency);

        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("StripeEventId is required.", nameof(stripeEventId));

        if (await IsAlreadyProcessedAsync(stripeEventId, eventType, cancellationToken).ConfigureAwait(false))
            return WebhookProcessResult.AlreadyProcessed;

        var action = _interpreter.Interpret(eventType);

        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment? payment = null;
        var stateChanged = false;
        string? failureReason = null;
        if (action == PaymentWebhookAction.Ignore)
        {
            _logger.LogInformation("Unsupported webhook event type received, no state change. StripeEventId={StripeEventId}, EventType={EventType}", stripeEventId, eventType);
        }
        else
        {
            payment = await FindPaymentAsync(paymentIntentId, action, stripeEventId, eventType, cancellationToken).ConfigureAwait(false);
            if (payment is not null)
            {
                // FindPaymentAsync returns non-null only after a successful intent-id lookup,
                // so paymentIntentId is non-empty here; the suppression documents that invariant.
                stateChanged = TryApplyEvent(payment, action, paymentIntentId!, stripeEventId, eventType, amountMinor, currency, stripeFailureReason, activity, out failureReason);
            }
        }

        var processedEvent = new ProcessedWebhookEvent(Guid.NewGuid(), stripeEventId, eventType, DateTimeOffset.UtcNow);
        _dbContext.Set<ProcessedWebhookEvent>().Add(processedEvent);

        // 9.11: map domain state change → integration event + Outbox in same transaction
        // Event interpretation already done via Application interpreter; outbox creation now
        // depends on the interpreted action + resulting domain status, not raw Stripe string.
        if (payment != null && stateChanged)
            StageOutbox(payment, action, failureReason, processedEvent, paymentIntentId, stripeEventId);

        return await CommitAsync(processedEvent, stripeEventId, eventType, activity, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsAlreadyProcessedAsync(string stripeEventId, string? eventType, CancellationToken cancellationToken)
    {
        var alreadyExists = await _dbContext.Set<ProcessedWebhookEvent>()
            .AsNoTracking()
            .AnyAsync(e => e.StripeEventId == stripeEventId, cancellationToken);

        if (alreadyExists)
            _logger.LogInformation("Duplicate webhook event detected before insert. StripeEventId={StripeEventId}, EventType={EventType}", stripeEventId, eventType);

        return alreadyExists;
    }

    private async Task<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment?> FindPaymentAsync(
        string? paymentIntentId,
        PaymentWebhookAction action,
        string stripeEventId,
        string? eventType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            _logger.LogWarning("Supported webhook {EventType} ({Action}) arrived without PaymentIntentId. StripeEventId={StripeEventId}", eventType, action, stripeEventId);
            return null;
        }

        var payment = await _dbContext.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>()
            .FirstOrDefaultAsync(p => p.ProviderPaymentIntentId == paymentIntentId, cancellationToken);

        if (payment is null)
            _logger.LogWarning("Payment not found for webhook. StripeEventId={StripeEventId}, EventType={EventType}, PaymentIntentId={PaymentIntentId}", stripeEventId, eventType, paymentIntentId);

        return payment;
    }

    private bool TryApplyEvent(
        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment payment,
        PaymentWebhookAction action,
        string paymentIntentId,
        string stripeEventId,
        string? eventType,
        long? amountMinor,
        string? currency,
        string? stripeFailureReason,
        Activity? activity,
        out string? failureReason)
    {
        failureReason = null;

        if (IsAmountMismatch(payment, amountMinor, currency))
        {
            // Recorded as processed (Stripe stops retrying): a genuine amount
            // discrepancy never heals on redelivery and must not flip the Payment
            // nor emit an outbox event. Operators reconcile via the error log.
            activity?.SetTag("webhook.amount_mismatch", true);
            _logger.LogError(
                "Stripe webhook amount mismatch; event recorded without state change. StripeEventId={StripeEventId}, EventType={EventType}, PaymentId={PaymentId}, ExpectedAmount={ExpectedAmount} {ExpectedCurrency}, EventAmountMinor={EventAmountMinor}, EventCurrency={EventCurrency}",
                stripeEventId, eventType, payment.Id,
                payment.Amount, payment.Currency, amountMinor, currency);
            return false;
        }

        if (!amountMinor.HasValue || string.IsNullOrWhiteSpace(currency))
            _logger.LogWarning("Webhook arrived without amount details; skipping amount verification. StripeEventId={StripeEventId}, EventType={EventType}, PaymentId={PaymentId}", stripeEventId, eventType, payment.Id);

        var statusBefore = payment.Status;
        try
        {
            var stateChanged = ApplyEventToPayment(payment, action, paymentIntentId, stripeFailureReason, out failureReason);
            if (stateChanged)
                _logger.LogInformation("Payment state transition applied via webhook. StripeEventId={StripeEventId}, EventType={EventType}, Action={Action}, PaymentId={PaymentId}, {OldStatus}->{NewStatus}", stripeEventId, eventType, action, payment.Id, statusBefore, payment.Status);
            else
                _logger.LogInformation("Payment webhook event received but no state change required. StripeEventId={StripeEventId}, EventType={EventType}, Action={Action}, PaymentId={PaymentId}", stripeEventId, eventType, action, payment.Id);
            return stateChanged;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Out-of-order or invalid transition ignored. StripeEventId={StripeEventId}, EventType={EventType}, Action={Action}, PaymentId={PaymentId}, CurrentStatus={Status}", stripeEventId, eventType, action, payment.Id, payment.Status);
            return false;
        }
    }

    private void StageOutbox(
        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment payment,
        PaymentWebhookAction action,
        string? failureReason,
        ProcessedWebhookEvent processedEvent,
        string? paymentIntentId,
        string stripeEventId)
    {
        OutboxMessage? outbox = null;
        var now = DateTimeOffset.UtcNow;

        if (payment.Status == PaymentStatus.Succeeded && action == PaymentWebhookAction.Succeed)
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
            else if (payment.Status == PaymentStatus.Failed && action == PaymentWebhookAction.Fail)
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
            else if (payment.Status == PaymentStatus.Cancelled && action == PaymentWebhookAction.Cancel)
            {
                var evt = new PaymentCanceledIntegrationEvent(
                    payment.OrderId,
                    payment.Id,
                    payment.ProviderPaymentIntentId ?? paymentIntentId ?? string.Empty,
                    "Stripe reported payment_intent.canceled",
                    payment.Status.ToString(),
                    now)
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = payment.OrderId,
                    CausationId = processedEvent.Id,
                    OccurredAtUtc = now.UtcDateTime,
                    Version = FluxCommerce.BuildingBlocks.Messaging.Versioning.EventVersion.V1
                };
                outbox = _outboxFactory.Create(PaymentCanceledIntegrationEvent.EventType, evt);
            }

            if (outbox != null)
            {
                _dbContext.Set<OutboxMessage>().Add(outbox);
                _logger.LogInformation("Staged integration event {EventType} for Outbox. StripeEventId={StripeEventId}, PaymentId={PaymentId}, CorrelationId={CorrelationId}, CausationId={CausationId}", outbox.Type, stripeEventId, payment.Id, outbox.CorrelationId, outbox.CausationId);
            }
    }

    private async Task<WebhookProcessResult> CommitAsync(
        ProcessedWebhookEvent processedEvent,
        string stripeEventId,
        string? eventType,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return WebhookProcessResult.Processed;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Concurrent different-event race on the same Payment (Version token): neither event
            // was recorded, so rethrowing lets Stripe retry and converge on redelivery. Logged
            // distinctly from unique violations so the race is visible in alerting.
            activity?.SetTag("webhook.concurrency_conflict", true);
            _logger.LogWarning(ex, "Concurrent webhook write conflict; Stripe retry will converge. StripeEventId={StripeEventId}, EventType={EventType}", stripeEventId, eventType);
            throw;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(ex, "Concurrent duplicate webhook race handled via unique constraint. StripeEventId={StripeEventId}", stripeEventId);
            _dbContext.Entry(processedEvent).State = EntityState.Detached;
            return WebhookProcessResult.AlreadyProcessed;
        }
    }

    private static bool ApplyEventToPayment(
        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment payment,
        PaymentWebhookAction action,
        string paymentIntentId,
        string? stripeFailureReason,
        out string? failureReason)
    {
        failureReason = null;
        var before = payment.Status;
        switch (action)
        {
            case PaymentWebhookAction.Succeed:
                payment.Succeed(paymentIntentId);
                return payment.Status == PaymentStatus.Succeeded && before != PaymentStatus.Succeeded;
            case PaymentWebhookAction.Fail:
                failureReason = string.IsNullOrWhiteSpace(stripeFailureReason) ? "Stripe payment_failed" : stripeFailureReason;
                payment.Fail(failureReason);
                return payment.Status == PaymentStatus.Failed && before != PaymentStatus.Failed;
            case PaymentWebhookAction.Cancel:
                payment.Cancel();
                return payment.Status == PaymentStatus.Cancelled && before != PaymentStatus.Cancelled;
            case PaymentWebhookAction.Process:
                payment.Process();
                return payment.Status == PaymentStatus.Processing && before != PaymentStatus.Processing;
            default:
                return false;
        }
    }

    /// <summary>
    /// Compares the Stripe event amount (minor units) against the local Payment.
    /// Missing event amount/currency skips verification with a warning at the call
    /// site contract (not a mismatch): older or non-payment-intent payloads may not
    /// carry them, and rejecting those would break the supported allow-list.
    /// </summary>
    private static bool IsAmountMismatch(
        FluxCommerce.Modules.Payments.Domain.Aggregates.Payment payment,
        long? eventAmountMinor,
        string? eventCurrency)
    {
        if (!eventAmountMinor.HasValue || string.IsNullOrWhiteSpace(eventCurrency))
            return false;

        if (!string.Equals(payment.Currency, eventCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return CurrencyMinorUnits.ToMinorUnits(payment.Amount, payment.Currency) != eventAmountMinor.Value;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UX_ProcessedWebhookEvents_StripeEventId", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}

