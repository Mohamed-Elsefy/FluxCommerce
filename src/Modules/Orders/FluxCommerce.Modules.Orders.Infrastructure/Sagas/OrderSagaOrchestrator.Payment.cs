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
/// Payment leg of the saga: intent staging and payment outcomes (Phase 10).
/// </summary>
public sealed partial class OrderSagaOrchestrator
{

    /// <summary>
    /// Phase 10.3 stages <c>payments.create_payment_requested</c> after the saga enters
    /// <c>WaitingForPayment</c>. The event carries authoritative <c>Amount/Currency/CustomerId</c>
    /// from the Order snapshot and preserves correlation/causation. It is staged via the Orders
    /// outbox so it commits atomically with the saga state transition. Duplicate invocations
    /// are prevented by the caller checking saga state.
    /// </summary>
    private async Task<IReadOnlyList<CreatePaymentRequestedIntegrationEvent>> StagePaymentRequestAsync(
        OrderSaga saga,
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken)
    {
        var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            throw new InvalidOperationException($"Cannot stage payment for order '{orderId.Value}': the order does not exist.");
        }

        var producedAtUtc = _dateTimeProvider.UtcNow;
        var amount = order.GrandTotal.Amount;
        var currency = order.GrandTotal.Currency;
        var customerId = order.CustomerId.Value;

        var paymentEvent = new CreatePaymentRequestedIntegrationEvent(
            orderId.Value,
            customerId,
            amount,
            currency)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = producedAtUtc
        };

        _context.OutboxMessages.Add(_outboxMessageFactory.Create(paymentEvent));

        _logger.LogInformation(
            "Staged create_payment_requested for order {OrderId} (amount {Amount} {Currency}, customer {CustomerId}, correlation {CorrelationId}, causation {CausationId}).",
            orderId.Value,
            amount,
            currency,
            customerId,
            correlationId,
            causationId);

        return [paymentEvent];
    }

    /// <summary>
    /// Records the payment correlation staged by <c>payments.payment_initiated</c> on the saga
    /// via <c>MarkPaymentRequested</c> (bookkeeping only: no state advance, no version bump).
    /// Valid only in <c>WaitingForPayment</c>; the same payment id is an idempotent no-op while
    /// a different payment id is rejected as invalid so cross-payment contamination is never
    /// applied. Post-payment states acknowledge as duplicate (the tracking point has passed;
    /// refund and success paths carry their own ids). Saga changes commit atomically with the
    /// processed-message record via the Orders idempotent boundary.
    /// </summary>
    /// <exception cref="InvalidOperationException">No saga exists for the order (anomaly → retry/dead-letter).</exception>
    public async Task<OrderSagaTransitionResult> ApplyPaymentInitiatedAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(providerPaymentIntentId))
            throw new ArgumentException("A provider payment intent id is required.", nameof(providerPaymentIntentId));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply payment initiated for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentInitiated",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var sagaTransition = saga.MarkPaymentRequested(paymentId, providerPaymentIntentId, occurredAtUtc);
                if (sagaTransition.IsFailure)
                {
                    activity?.AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                    var (traceInv, spanInv) = CurrentTraceIds();
                    _logger.LogWarning(
                        "Saga {SagaId} for order {OrderId} already tracks a different payment; payments.payment_initiated for payment {PaymentId} rejected (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id, orderId.Value, paymentId, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());
                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_initiated to saga {SagaId} for order {OrderId} (payment {PaymentId}, intent {IntentId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, paymentId, providerPaymentIntentId, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Completed:
            case OrderSagaState.Failed:
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already past payment tracking ({SagaState}); payments.payment_initiated acknowledged without change (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceDef, spanDef) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} in state {SagaState} cannot record payment initiation (previous state {PreviousState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceDef, spanDef);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.4: handles <c>payments.payment_succeeded</c> — validates correlation with the active saga,
    /// verifies the saga is in <c>WaitingForPayment</c>, transitions saga via <c>SucceedPayment</c> to
    /// <c>Completed</c>, and atomically confirms the Order via <c>Confirm</c> (Pending → Confirmed).
    /// Duplicate delivery on <c>Completed</c> is an idempotent no-op (no re-confirm, no version bump);
    /// out-of-order or invalid states are rejected without corrupting state or throwing unhandled exceptions.
    /// Saga + Order changes commit atomically with the processed-message record via the Orders idempotent boundary.
    ///
    /// <para>
    /// Phase 11.7 handles the <c>PaymentSucceeded</c> after <c>ReservationExpired</c> race (money charged for
    /// an order whose reservation already expired and released Inventory). When the saga is already
    /// <c>Cancelled</c> (the expiry won before this success arrived), a success here means money was taken
    /// with no inventory behind it, so the orchestrator stages an explicit <c>refund_payment_requested</c>
    /// compensation instead of merely rejecting it — preventing money lock-in without double-charging or
    /// resurrecting the terminal workflow. The saga stays <c>Cancelled</c> (no backward move, no version bump)
    /// and the Order is NOT confirmed. The refund is staged atomically inside the same Orders idempotent
    /// boundary as the processed-message record, so a redelivered success does not stage a duplicate refund.
    /// </para>
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentSucceededAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
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
        {
            throw new InvalidOperationException($"Cannot apply payment succeeded for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentSucceeded",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var correlationMismatch = PaymentCorrelationMismatch(saga, paymentId, providerPaymentIntentId);
                if (correlationMismatch is not null)
                {
                    activity?.AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                    var (traceMismatch, spanMismatch) = CurrentTraceIds();
                    _logger.LogWarning(
                        "Saga {SagaId} for order {OrderId} rejected payments.payment_succeeded: {Reason} (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id, orderId.Value, correlationMismatch, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceMismatch, spanMismatch);
                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
                }

                var sagaTransition = saga.SucceedPayment(occurredAtUtc);
                if (sagaTransition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not succeed payment: {sagaTransition.Error.Code}.");
                }

                var order = await _orderStore.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                if (order is null)
                {
                    throw new InvalidOperationException($"Cannot confirm order '{orderId.Value}': the order does not exist.");
                }

                var confirm = order.Confirm(occurredAtUtc);
                if (confirm.IsFailure)
                {
                    // Already confirmed → idempotent, keep confirmed state
                    _logger.LogInformation("Order {OrderId} was already confirmed when applying payment_succeeded; keeping confirmed state.", orderId.Value);
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_succeeded to saga {SagaId} for order {OrderId} (payment {PaymentId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id, orderId.Value, paymentId, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Completed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate payments.payment_succeeded acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Failed:
            case OrderSagaState.ReleasingInventory:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConf, spanConf) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_succeeded cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceConf, spanConf);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            case OrderSagaState.Cancelled:
            {
                // Phase 11.7 race: PaymentSucceeded arrived AFTER ReservationExpired (the saga is already
                // Cancelled and the order already cancelled). Money was charged, but the reservation has
                // expired and Inventory has already released it — there is no inventory to fulfil, so the
                // payment must be refunded to prevent money lock-in. The desired outcome is determined by
                // the persisted saga state and explicit rules, not by message arrival order: the terminal
                // Cancelled saga is NOT moved backward and the order is NOT confirmed; only the refund is
                // staged, atomically with the processed-message record (so a redelivered success does not
                // stage a duplicate refund).
                var cancelledOrder = await _orderStore
                    .GetByIdAsync(orderId, cancellationToken)
                    .ConfigureAwait(false);

                if (cancelledOrder is null)
                    throw new InvalidOperationException(
                        $"Cannot refund order '{orderId.Value}': the order does not exist.");

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var refundEvent = new RefundPaymentRequestedIntegrationEvent(
                    orderId.Value,
                    paymentId,
                    providerPaymentIntentId,
                    cancelledOrder.GrandTotal.Amount,
                    cancelledOrder.GrandTotal.Currency,
                    "ReservationExpired.BeforePaymentSucceeded")
                {
                    EventId = Guid.NewGuid(),
                    CorrelationId = correlationId,
                    CausationId = causationId,
                    OccurredAtUtc = producedAtUtc
                };
                _context.OutboxMessages.Add(_outboxMessageFactory.Create(refundEvent));

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConflict, spanConflict) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} was already cancelled when payments.payment_succeeded arrived (inventory reservation expired); staged {RefundEvent} to refund the charged payment {PaymentId} and prevent money lock-in (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}); order stays {OrderStatus}.",
                    saga.Id,
                    orderId.Value,
                    RefundPaymentRequestedIntegrationEvent.EventType,
                    paymentId,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Conflicting,
                    traceConflict,
                    spanConflict,
                    cancelledOrder.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            }
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_succeeded cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Handles <c>payments.payment_canceled</c> — validates correlation with the active saga,
    /// verifies the saga is in <c>WaitingForPayment</c>, transitions saga via <c>FailPayment</c> to
    /// <c>ReleasingInventory</c> (a canceled intent can no longer pay the order), and atomically stages
    /// <c>inventory.release_requested</c> for each recorded reservation via the Orders outbox.
    /// Duplicate delivery on <c>ReleasingInventory</c>/<c>Cancelled</c> is an idempotent no-op;
    /// out-of-order after <c>Completed</c>/<c>Failed</c> or a mismatched payment correlation is a
    /// conflicting rejection. Saga + outbox commit atomically via the Orders idempotent boundary.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentCanceledAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
        string reason,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply payment canceled for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentCanceled",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var correlationMismatch = PaymentCorrelationMismatch(saga, paymentId, providerPaymentIntentId);
                if (correlationMismatch is not null)
                {
                    activity?.AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                    var (traceMismatch, spanMismatch) = CurrentTraceIds();
                    _logger.LogWarning(
                        "Saga {SagaId} for order {OrderId} rejected payments.payment_canceled: {Reason} (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id, orderId.Value, correlationMismatch, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceMismatch, spanMismatch);
                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
                }

                var sagaTransition = saga.FailPayment(occurredAtUtc, reason);
                if (sagaTransition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not cancel payment: {sagaTransition.Error.Code}.");
                }

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var reservationIds = saga.ReservationIds.ToList();
                var releaseCommands = new List<InventoryReleaseRequestedIntegrationEvent>();

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
                    releaseCommands.Add(release);
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_canceled to saga {SagaId} for order {OrderId} (payment {PaymentId}, reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); staged {ReleaseCount} release(s).",
                    saga.Id, orderId.Value, paymentId, reason, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, releaseCommands.Count);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already releasing inventory; duplicate payments.payment_canceled acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
            case OrderSagaState.Failed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConf, spanConf) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_canceled cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceConf, spanConf);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} in state {SagaState}; payments.payment_canceled cannot be applied (previous state {PreviousState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }

    /// <summary>
    /// Phase 10.5: handles <c>payments.payment_failed</c> — validates correlation with the active saga,
    /// verifies the saga is in <c>WaitingForPayment</c>, transitions saga via <c>FailPayment</c> to
    /// <c>ReleasingInventory</c>, and atomically stages <c>inventory.release_requested</c> for each
    /// recorded reservation via the Orders outbox. Duplicate delivery on <c>ReleasingInventory</c>/<c>Cancelled</c>
    /// is an idempotent no-op (no duplicate release); out-of-order after <c>Completed</c>/<c>Failed</c>
    /// is a conflicting rejection. Saga + outbox commit atomically via the Orders idempotent boundary.
    /// </summary>
    public async Task<OrderSagaTransitionResult> ApplyPaymentFailedAsync(
        OrderId orderId,
        Guid paymentId,
        string providerPaymentIntentId,
        string failureReason,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        if (paymentId == Guid.Empty)
            throw new ArgumentException("A payment id must not be an empty GUID.", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("A failure reason is required.", nameof(failureReason));
        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (saga is null)
        {
            throw new InvalidOperationException($"Cannot apply payment failed for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var activity = StartSagaActivity(
            "Orders.Saga.ApplyPaymentFailed",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForPayment:
            {
                var correlationMismatch = PaymentCorrelationMismatch(saga, paymentId, providerPaymentIntentId);
                if (correlationMismatch is not null)
                {
                    activity?.AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                    var (traceMismatch, spanMismatch) = CurrentTraceIds();
                    _logger.LogWarning(
                        "Saga {SagaId} for order {OrderId} rejected payments.payment_failed: {Reason} (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id, orderId.Value, correlationMismatch, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceMismatch, spanMismatch);
                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
                }

                var sagaTransition = saga.FailPayment(occurredAtUtc, failureReason);
                if (sagaTransition.IsFailure)
                {
                    throw new InvalidOperationException($"Saga for order '{orderId.Value}' could not fail payment: {sagaTransition.Error.Code}.");
                }

                var producedAtUtc = _dateTimeProvider.UtcNow;
                var reservationIds = saga.ReservationIds.ToList();
                var releaseCommands = new List<InventoryReleaseRequestedIntegrationEvent>();

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
                    releaseCommands.Add(release);
                }

                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied payments.payment_failed to saga {SagaId} for order {OrderId} (payment {PaymentId}, reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); staged {ReleaseCount} release(s).",
                    saga.Id, orderId.Value, paymentId, failureReason, correlationId, causationId, previousState, saga.State, OrderSagaTransitionOutcome.Applied, traceId, spanId, releaseCommands.Count);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.ReleasingInventory:
            case OrderSagaState.Cancelled:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());
                var (traceDup, spanDup) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already releasing inventory; duplicate payments.payment_failed acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, previousState, saga.State, OrderSagaTransitionOutcome.Duplicate, traceDup, spanDup);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
            case OrderSagaState.Failed:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());
                var (traceConf, spanConf) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Conflicting, traceConf, spanConf);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                activity?.AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
                var (traceInv, spanInv) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; payments.payment_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id, orderId.Value, saga.State, previousState, saga.State, OrderSagaTransitionOutcome.Invalid, traceInv, spanInv);
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }
    /// <summary>
    /// Compares a payment result event against the payment correlation recorded on the saga
    /// (<c>MarkPaymentRequested</c> via <c>payments.payment_initiated</c>). Sagas that never
    /// recorded a correlation (legacy rows) accept any payment id. Returns a human-readable
    /// reason when the event belongs to a different payment, otherwise null.
    /// </summary>
    private static string? PaymentCorrelationMismatch(OrderSaga saga, Guid paymentId, string? providerPaymentIntentId)
    {
        if (saga.PaymentId.HasValue && saga.PaymentId.Value != paymentId)
            return $"tracked payment {saga.PaymentId.Value} does not match event payment {paymentId}";

        if (!string.IsNullOrWhiteSpace(saga.ProviderPaymentIntentId)
            && !string.IsNullOrWhiteSpace(providerPaymentIntentId)
            && !string.Equals(saga.ProviderPaymentIntentId, providerPaymentIntentId, StringComparison.Ordinal))
            return "tracked provider intent does not match event intent";

        return null;
    }
}