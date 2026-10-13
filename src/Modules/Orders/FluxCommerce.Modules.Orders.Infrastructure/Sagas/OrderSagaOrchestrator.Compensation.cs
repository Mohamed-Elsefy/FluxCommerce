using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Compensation leg of the saga: inventory release and payment refunds (Phase 8.7/10.8).
/// </summary>
public sealed partial class OrderSagaOrchestrator
{

    /// <summary>
    /// Phase 8.7 issues the compensation release when an order is cancelled after its reservations were
    /// applied (ADR-009): for every reservation recorded on the saga it stages an
    /// <see cref="InventoryReleaseRequestedIntegrationEvent"/>, so Inventory returns the reserved
    /// quantity to available stock. Inventory owns the release — the saga only requests it through the
    /// explicit contract and never touches reservation state. The commands are staged for the caller's
    /// unit of work, so they commit atomically with the processed-message record; under at-least-once
    /// delivery a redelivered cancellation is skipped by the idempotent-processing boundary and the
    /// Inventory release consumer is itself idempotent (a repeated release of a terminal reservation is
    /// a benign no-op). The saga is never transitioned and the version is never bumped — release is a
    /// fire-and-forget command — but the saga IS marked as release-requested (Phase 8.8), so a late
    /// <c>inventory.reserved</c> result arriving after this point is compensated immediately. A missing
    /// saga is a benign no-op (the workflow never reserved inventory for this order, e.g. the 8.6
    /// failure path already cancelled it); a saga with no recorded reservations is also a no-op (no
    /// release command), but it is still marked so a reservation that lands later is released. Only an
    /// order with no saga AND no recorded reservations is indistinguishable from the no-reservations
    /// case, so no throw is needed here.
    /// </summary>
    /// <param name="orderId">The cancelled order whose reservations must be released.</param>
    /// <param name="correlationId">The correlation id carried by the consumed <c>orders.order_cancelled</c> message.</param>
    /// <param name="causationId">The consumed message's event id, becoming the commands' causation id.</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaReleaseResult> RequestReleaseAsync(
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            var (noSagaTraceId, noSagaSpanId) = CurrentTraceIds();
            _logger.LogInformation(
                "No saga exists for cancelled order {OrderId}; no inventory release requested (correlation {CorrelationId}, trace {TraceId}, span {SpanId}).",
                orderId.Value,
                correlationId,
                noSagaTraceId,
                noSagaSpanId);

            return new OrderSagaReleaseResult(SagaFound: false, ReleaseIssued: false, SagaId: null, Commands: []);
        }

        var previousState = saga.State;
        using var releaseActivity = StartSagaActivity(
            "Orders.Saga.RequestRelease",
            orderId,
            saga.Id,
            previousState,
            previousState,
            correlationId,
            causationId);

        var producedAtUtc = _dateTimeProvider.UtcNow;

        // Phase 8.8: mark the saga as release-requested BEFORE staging the commands, so a late
        // inventory.reserved result that races this cancellation is compensated immediately. The marker
        // is bookkeeping (no state transition, no version bump); the first request wins and a redelivered
        // cancellation cannot shift it.
        saga.MarkReleaseRequested(producedAtUtc);

        var reservationIds = saga.ReservationIds.ToList();

        if (reservationIds.Count == 0)
        {
            releaseActivity?.AddTag("saga.outcome", "release-not-issued");

            var (noReservationTraceId, noReservationSpanId) = CurrentTraceIds();
            _logger.LogInformation(
                "Saga {SagaId} for cancelled order {OrderId} has no recorded reservations; no inventory release requested (correlation {CorrelationId}, trace {TraceId}, span {SpanId}).",
                saga.Id,
                orderId.Value,
                correlationId,
                noReservationTraceId,
                noReservationSpanId);

            return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: false, saga.Id, []);
        }

        var commands = reservationIds
            .Select(id => new InventoryReleaseRequestedIntegrationEvent(id)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = correlationId,
                CausationId = causationId,
                OccurredAtUtc = producedAtUtc
            })
            .ToList();

        foreach (var command in commands)
            _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        releaseActivity?.AddTag("saga.outcome", "release-issued");

        var (traceId, spanId) = CurrentTraceIds();
        _logger.LogInformation(
            "Requested release of {ReservationCount} reservation(s) for cancelled order {OrderId} (saga {SagaId}, correlation {CorrelationId}, causation {CausationId}, state {PreviousState} → {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
            commands.Count,
            orderId.Value,
            saga.Id,
            correlationId,
            causationId,
            previousState,
            saga.State,
            "release-issued",
            traceId,
            spanId);

        return new OrderSagaReleaseResult(SagaFound: true, ReleaseIssued: true, saga.Id, commands);
    }

    /// <summary>
    /// Phase 10.6: handles <c>inventory.released</c> — validates correlation with the active saga,
    /// transitions saga via <c>MarkInventoryReleased</c> from <c>ReleasingInventory</c> to <c>Cancelled</c>
    /// (OrderCancelled) and preserves correlation/causation propagation. Duplicate delivery on
    /// <c>Cancelled</c>/<c>Completed</c> is an idempotent no-op; out-of-order after <c>Completed</c> is a
    /// duplicate; after <c>Failed</c>/<c>WaitingForPayment</c> etc. is invalid. Saga change commits
    /// atomically with the processed-message record via the Orders idempotent boundary. No Order status
    /// mutation here — cancellation finalization is 10.7.
    /// </summary>
    /// <remarks>
    /// Phase 10.7 extends this handler to automatically cancel the Order and finalize the saga
    /// (<c>Cancelled → Completed</c>) within the same database boundary. After <c>MarkInventoryReleased</c>
    /// transitions to <c>Cancelled</c>, the Order aggregate is cancelled via <c>Order.Cancel()</c>
    /// (Pending → Cancelled, idempotent on already-Cancelled) and the saga is finalized via
    /// <c>FinalizeCancellation()</c> (Cancelled → Completed). The saga strictly does NOT reach
    /// <c>Completed</c> before both inventory release and order cancellation are satisfied; retry
    /// while in <c>ReleasingInventory</c> leaves saga there, and duplicate <c>inventory.released</c>
    /// on <c>Cancelled</c>/<c>Completed</c> is idempotent without re-cancelling or bumping version.
    /// All changes (saga + order + outbox OrderCancelled) commit atomically with the processed-message.
    /// </remarks>
    public async Task<OrderSagaTransitionResult> ApplyInventoryReleasedAsync(
        OrderId orderId,
        Guid reservationId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (reservationId == Guid.Empty)
            throw new ArgumentException("A reservation id must not be an empty GUID.", nameof(reservationId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply inventory released for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyInventoryReleased",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId,
            reservationId: reservationId);

        switch (saga.State)
        {
            case OrderSagaState.ReleasingInventory:
            {
                var transition = saga.MarkInventoryReleased(occurredAtUtc);
                if (transition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not mark inventory released: {transition.Error.Code}.");
                }

                // Phase 10.7: automatically cancel Order within same DB boundary
                // Phase 10.8: also handle Confirmed → Cancelled for post-payment refund
                var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (order is null)
                {
                    throw new InvalidOperationException($"Cannot cancel order '{orderId.Value}': the order does not exist.");
                }

                var cancel = order.Cancel(occurredAtUtc);
                if (cancel.IsFailure)
                {
                    var refundCancel = order.CancelForRefund(occurredAtUtc);
                    if (refundCancel.IsFailure)
                    {
                        _logger.LogInformation("Order {OrderId} was already cancelled when applying inventory.released; keeping cancelled state.", orderId.Value);
                    }
                }

                // Phase 10.7: finalize saga only after both release and cancellation succeeded
                var finalize = saga.FinalizeCancellation(occurredAtUtc);
                if (finalize.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not finalize cancellation: {finalize.Error.Code}.");
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.released to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id, orderId.Value, reservationId, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Cancelled:
            {
                var orderCancelled = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (orderCancelled is not null && (orderCancelled.Status == OrderStatus.Pending || orderCancelled.Status == OrderStatus.Confirmed))
                {
                    var cancel = orderCancelled.Status == OrderStatus.Pending ? orderCancelled.Cancel(occurredAtUtc) : orderCancelled.CancelForRefund(occurredAtUtc);
                    if (cancel.IsFailure)
                    {
                        _logger.LogInformation("Order {OrderId} was already cancelled on duplicate inventory.released (Cancelled state).", orderId.Value);
                    }
                }

                var finalize = saga.FinalizeCancellation(occurredAtUtc);
                if (finalize.IsFailure && saga.State != OrderSagaState.Completed)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not finalize on duplicate: {finalize.Error.Code}.");
                }

                var outcome = saga.State == OrderSagaState.Completed && previousState == OrderSagaState.Cancelled ? OrderSagaTransitionOutcome.Applied : OrderSagaTransitionOutcome.Duplicate;
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", outcome.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already cancelled; duplicate inventory.released handled (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, outcome, traceDup, spanDup);
                return new OrderSagaTransitionResult(outcome, saga.Id);
            }
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceCompletedDup, spanCompletedDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate inventory.released acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceCompletedDup, spanCompletedDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Failed:
            case OrderSagaState.WaitingForPayment:
            case OrderSagaState.WaitingForInventory:
            case OrderSagaState.Started:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv2, spanInv2) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.released cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv2, spanInv2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.released cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.8: trigger refund after post-payment downstream failure.
    /// Valid when saga is Completed (payment succeeded, order confirmed) — transitions
    /// Completed → Refunding and stages RefundPaymentRequested via Orders outbox.
    /// Duplicate on Refunding/ReleasingInventory/Cancelled is idempotent; already Completed
    /// second time after refund is also duplicate. Preserves W3C tracing.
    /// </summary>
    public async Task<OrderSagaTransitionResult> RequestRefundAsync(
        OrderId orderId,
        string reason,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
            throw new InvalidOperationException($"Cannot request refund for order '{orderId.Value}': no saga exists.");

        var previousState = saga.State;
        using var activity = StartSagaActivity("Orders.Saga.RequestRefund", orderId, saga.Id, previousState, correlationId: correlationId, causationId: causationId);

        // Load order to get authoritative amount/currency/payment info
        var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            throw new InvalidOperationException($"Cannot request refund for order '{orderId.Value}': order not found.");

        // The refund event must carry the tracked payment correlation recorded via
        // payments.payment_initiated. Staging with a placeholder would fail Payments-side
        // validation (and retry until dead-lettered), so untracked sagas fail fast here.
        if (saga.PaymentId is not { } paymentId || string.IsNullOrWhiteSpace(saga.ProviderPaymentIntentId))
            throw new InvalidOperationException($"Cannot request refund for order '{orderId.Value}': no payment correlation is tracked on the saga.");

        var providerIntentId = saga.ProviderPaymentIntentId;
        var amount = order.GrandTotal.Amount;
        var currency = order.GrandTotal.Currency;

        switch (saga.State)
        {
            case OrderSagaState.Completed:
            {
                var transition = saga.RequestRefund(_dateTimeProvider.UtcNow);
                if (transition.IsFailure)
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not request refund: {transition.Error.Code}.");

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var refundEvent = new RefundPaymentRequestedIntegrationEvent(orderId.Value, paymentId, providerIntentId, amount, currency, reason)
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = correlationId,
                    CausationId = causationId,
                    OccurredAtUtc = producedAtUtc
                };
                _context.OutboxMessages.Add(_outboxMessageFactory.Create(refundEvent));

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());
                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation("Staged refund_payment_requested for order {OrderId} saga {SagaId} (previous {PreviousState}→{NewState}, correlation {CorrelationId}, trace {TraceId})",
                    orderId.Value, saga.Id, previousState, saga.State, correlationId, traceId);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Refunding:
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation("Saga {SagaId} for order {OrderId} already in {SagaState}; duplicate refund request acknowledged (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Duplicate, traceDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv2, spanInv2) = CurrentTraceIds();
                _logger.LogWarning("Saga {SagaId} for order {OrderId} in state {SagaState} cannot request refund (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceInv2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.8: handles PaymentRefunded — Refunding → ReleasingInventory, then stages
    /// inventory.release_requested for each reservation. Duplicate on ReleasingInventory/Cancelled/Completed is idempotent.
    /// Preserves correlation/causation and W3C tracing.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentRefundedAsync(
        OrderId orderId,
        Guid paymentId,
        string? providerRefundId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
            throw new InvalidOperationException($"Cannot apply payment refunded for order '{orderId.Value}': no saga exists.");

        var previousState = saga.State;
        using var activity = StartSagaActivity("Orders.Saga.ApplyPaymentRefunded", orderId, saga.Id, previousState, correlationId: correlationId, causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.Refunding:
            {
                var transition = saga.HandlePaymentRefunded(occurredAtUtc);
                if (transition.IsFailure)
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not handle payment refunded: {transition.Error.Code}.");

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var reservationIds = saga.ReservationIds.ToList();
                foreach (var reservationId in reservationIds)
                {
                    var release = new InventoryReleaseRequestedIntegrationEvent(reservationId)
                    {
                        EventId = Guid.NewGuid(),
                        CorrelationId = correlationId,
                        CausationId = causationId,
                        OccurredAtUtc = producedAtUtc
                    };
                    _context.OutboxMessages.Add(_outboxMessageFactory.Create(release));
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());
                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation("Applied payment_refunded to saga {SagaId} for order {OrderId} (payment {PaymentId}, refund {RefundId}, correlation {CorrelationId}): {PreviousState}→{NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}, staged {ReleaseCount} releases)",
                    saga.Id, orderId.Value, paymentId, providerRefundId, correlationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, reservationIds.Count);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup2, spanDup2) = CurrentTraceIds();
                _logger.LogInformation("Saga {SagaId} for order {OrderId} already in {SagaState}; duplicate payment_refunded acknowledged (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Duplicate, traceDup2);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv3, spanInv3) = CurrentTraceIds();
                _logger.LogWarning("Saga {SagaId} for order {OrderId} in state {SagaState} cannot apply payment_refunded (previous {PreviousState}, outcome {Outcome}, trace {TraceId})",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceInv3);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }
    /// <summary>
    /// Phase 8.8 stages an immediate compensation release for a reservation that was recorded on a saga
    /// whose release was already requested (the order was cancelled while the reservation result was
    /// still in flight). The command carries the consumed result's correlation/causation ids and is
    /// staged for the caller's unit of work, so it commits atomically with the saga change and the
    /// processed-message record.
    /// </summary>
    private IReadOnlyList<InventoryReleaseRequestedIntegrationEvent> StageImmediateRelease(
        Guid reservationId,
        Guid correlationId,
        Guid? causationId)
    {
        var command = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = _dateTimeProvider.UtcNow
        };

        _context.OutboxMessages.Add(_outboxMessageFactory.Create(command));

        _logger.LogInformation(
            "Staged an immediate release for reservation {ReservationId} recorded after the compensation release was requested (correlation {CorrelationId}, causation {CausationId}); the reservation is compensated rather than left to expire.",
            reservationId,
            correlationId,
            causationId);

        return [command];
    }
}