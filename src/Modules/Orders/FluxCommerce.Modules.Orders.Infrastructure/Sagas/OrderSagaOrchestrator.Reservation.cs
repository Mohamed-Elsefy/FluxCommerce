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
/// Reservation leg of the saga: success, expiry, and failure outcomes (Phase 8.6/11).
/// </summary>
public sealed partial class OrderSagaOrchestrator
{

    /// <summary>
    /// Applies an <c>inventory.reserved</c> result to the saga.
    /// Phase 8.6: <c>WaitingForInventory → Completed</c> (legacy).
    /// Phase 10.3: <c>WaitingForInventory → WaitingForPayment</c> and stages
    /// <c>payments.create_payment_requested</c> via the Orders outbox. The Order is
    /// left Pending — saga progress lives in saga state. Duplicate results on the
    /// target state are idempotent no-ops; conflicting results on terminal states
    /// are rejected. Stage is atomic with the processed-message record.
    ///
    /// <para>
    /// Phase 8.7: reservation identifiers are recorded on the saga for compensation.
    /// Phase 8.8: late reservations racing a release are compensated immediately.
    /// Phase 10.3: after the first reservation transitions to <c>WaitingForPayment</c>,
    /// a <c>CreatePaymentRequested</c> event is staged with authoritative
    /// <c>Amount/Currency/CustomerId</c> from the Order snapshot, carrying the same
    /// correlation/causation and published via the Orders outbox. Duplicate
    /// <c>inventory.reserved</c> on <c>WaitingForPayment</c> is an idempotent no-op
    /// (no duplicate payment request).
    /// </para>
    /// </summary>
    /// <param name="orderId">The order whose reservation succeeded.</param>
    /// <param name="reservationId">The identifier of the reservation that was created.</param>
    /// <param name="correlationId">The correlation id carried by the consumed result message.</param>
    /// <param name="causationId">The consumed result message's event id.</param>
    /// <param name="occurredAtUtc">The moment the reservation succeeded (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaTransitionResult> ApplyReservationSuccessAsync(
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

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            throw new InvalidOperationException(
                $"Cannot apply a reservation success for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var successActivity = StartSagaActivity(
            "Orders.Saga.ApplyReservationSuccess",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId,
            reservationId: reservationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForInventory:
            {
                var transition = saga.MarkWaitingForPayment(occurredAtUtc);

                if (transition.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Saga for order '{orderId.Value}' could not enter {OrderSagaState.WaitingForPayment}: {transition.Error.Code}.");
                }

                var newlyRecorded = saga.RecordReservation(reservationId);

                // Phase 8.8: late reservation racing a release must be compensated immediately.
                if (newlyRecorded && saga.ReleaseRequestedAtUtc is not null)
                {
                    var releaseCommands = StageImmediateRelease(reservationId, correlationId, causationId);
                    successActivity?
                        .AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                    var (traceId, spanId) = CurrentTraceIds();
                    _logger.LogInformation(
                        "Applied inventory.reserved to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id,
                        orderId.Value,
                        reservationId,
                        correlationId,
                        causationId,
                        previousState,
                        saga.State,
                        OrderSagaTransitionOutcome.Applied,
                        traceId,
                        spanId);

                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id, releaseCommands);
                }

                // Phase 10.3: stage payment initiation exactly once (first reservation).
                var paymentCommands = await StagePaymentRequestAsync(saga, orderId, correlationId, causationId, cancellationToken).ConfigureAwait(false);

                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId2, spanId2) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.reserved to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    reservationId,
                    correlationId,
                    causationId,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Applied,
                    traceId2,
                    spanId2);

                // Return with empty release commands; payment request is via outbox side-effect.
                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id, []);
            }
            case OrderSagaState.WaitingForPayment:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnWaiting, spanIdOnWaiting) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already waiting for payment; duplicate inventory.reserved acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnWaiting,
                    spanIdOnWaiting);

                var newlyRecordedOnWaiting = saga.RecordReservation(reservationId);

                var commandsOnWaiting = newlyRecordedOnWaiting && saga.ReleaseRequestedAtUtc is not null
                    ? StageImmediateRelease(reservationId, correlationId, causationId)
                    : [];

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id, commandsOnWaiting);
            case OrderSagaState.Completed:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnCompleted, spanIdOnCompleted) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already completed; duplicate inventory.reserved acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnCompleted,
                    spanIdOnCompleted);

                var newlyRecordedOnCompleted = saga.RecordReservation(reservationId);

                var commandsOnCompleted = newlyRecordedOnCompleted && saga.ReleaseRequestedAtUtc is not null
                    ? StageImmediateRelease(reservationId, correlationId, causationId)
                    : [];

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id, commandsOnCompleted);
            case OrderSagaState.Failed:
                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());

                var (traceIdOnFailed, spanIdOnFailed) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already failed; conflicting inventory.reserved rejected (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Conflicting,
                    traceIdOnFailed,
                    spanIdOnFailed);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
            {
                // A reservation arriving after compensation started (or after cancellation) proves
                // stock is held for this order while nobody will release it: the release sweep only
                // covers recorded reservations. Record the latecomer and release it immediately so
                // no inventory leaks. Returning Duplicate keeps the terminal workflow untouched.
                if (saga.State is OrderSagaState.ReleasingInventory
                    or OrderSagaState.Refunding
                    or OrderSagaState.Cancelled)
                {
                    var newlyRecordedLate = saga.RecordReservation(reservationId);
                    var lateCommands = newlyRecordedLate
                        ? StageImmediateRelease(reservationId, correlationId, causationId)
                        : [];

                    successActivity?
                        .AddTag("saga.new.state", saga.State.ToString())
                        .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                    var (traceIdLate, spanLate) = CurrentTraceIds();
                    _logger.LogInformation(
                        "Saga {SagaId} for order {OrderId} received a late inventory.reserved in {SagaState}; reservation recorded and released immediately (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                        saga.Id,
                        orderId.Value,
                        saga.State,
                        previousState,
                        saga.State,
                        OrderSagaTransitionOutcome.Duplicate,
                        traceIdLate,
                        spanLate);

                    return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id, lateCommands);
                }

                successActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());

                var (traceIdOnInvalid, spanIdOnInvalid) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reserved cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    saga.State,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Invalid,
                    traceIdOnInvalid,
                    spanIdOnInvalid);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
            }
        }
    }

    /// <summary>
    /// Phase 11.6 applies an <c>inventory.reservation_expired</c> result to the saga. It is the real
    /// counterpart of the Phase 11.4 stub: it wires the Phase 11.5 state model
    /// (<see cref="OrderSaga.ApplyReservationExpired"/>) into the consumer pipeline and, on a valid expiry,
    /// cancels the order through its own domain transition.
    /// <para>
    /// Behavior per saga state:
    /// <list type="bullet">
    /// <item><c>WaitingForPayment</c> — valid: <c>WaitingForPayment → Cancelled</c> (the reservation's stock
    /// is already released by Inventory, so the order cannot be fulfilled) and the Order transitions through
    /// its own <c>Cancel</c> (Pending → Cancelled), which raises the <c>OrderCancelled</c> domain event that
    /// the Orders outbox publishes as <c>orders.order_cancelled</c> atomically with the saga state. Returns
    /// <see cref="OrderSagaTransitionOutcome.Applied"/>.</item>
    /// <item><c>Completed</c> / <c>Failed</c> / <c>Cancelled</c> — terminal. Ignored: no saga state change, no
    /// Order mutation, no outbox event; a late expiry never moves a terminal saga backward.</item>
    /// <item><c>ReleasingInventory</c> / <c>Refunding</c> — in-flight compensation. Ignored (a release/refund is
    /// already being processed; do not restart or duplicate it).</item>
    /// <item><c>WaitingForInventory</c> — unexpected. Ignored safely (no result arrived before expiry).</item>
    /// <item><c>Started</c> — unreachable anomaly. Acknowledged without retrying (<see cref="OrderSagaTransitionOutcome.Invalid"/>).</item>
    /// </list>
    /// The saga transition and (on a valid expiry) the Order cancellation are staged in the same unit of work
    /// as the Orders idempotent-processing boundary, so they commit atomically with the processed-message
    /// record. An expired event for an order with no saga throws (anomaly → retry/dead-letter).
    /// </para>
    /// </summary>
    /// <param name="orderId">The order whose reservation expired.</param>
    /// <param name="reservationId">The expired reservation's identifier.</param>
    /// <param name="correlationId">The correlation id carried by the consumed message.</param>
    /// <param name="causationId">The consumed message's event id.</param>
    /// <param name="occurredAtUtc">The moment the reservation expired (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    /// <exception cref="InvalidOperationException">No saga exists for the order (anomaly → retry/dead-letter).</exception>
    public async Task<OrderSagaTransitionResult> ApplyReservationExpiredAsync(
        OrderId orderId,
        Guid reservationId,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            throw new InvalidOperationException(
                $"Cannot apply a reservation expiration for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var expiryActivity = StartSagaActivity(
            "Orders.Saga.ApplyReservationExpired",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId,
            reservationId: reservationId);

        var transition = saga.ApplyReservationExpired(occurredAtUtc);

        if (transition.IsFailure)
        {
            // Started is the only non-ignore state that cannot accept the expiry; acknowledged as an
            // unreachable anomaly without retrying (matched by the Phase 11.5 matrix).
            expiryActivity?
                .AddTag("saga.new.state", saga.State.ToString())
                .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());
            var (traceIdOnInvalid, spanIdOnInvalid) = CurrentTraceIds();
            _logger.LogWarning(
                "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reservation_expired cannot be applied (correlation {CorrelationId}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                saga.Id,
                orderId.Value,
                saga.State,
                correlationId,
                OrderSagaTransitionOutcome.Invalid,
                traceIdOnInvalid,
                spanIdOnInvalid);

            return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }

        if (previousState != OrderSagaState.WaitingForPayment)
        {
            // Safe ignore: terminal (Completed/Failed/Cancelled), in-flight compensation
            // (ReleasingInventory/Refunding) or unexpected (WaitingForInventory) — no saga state change,
            // no Order mutation, no outbox event.
            expiryActivity?
                .AddTag("saga.new.state", saga.State.ToString())
                .AddTag("saga.outcome", OrderSagaTransitionOutcome.Ignored.ToString());
            var (traceIdOnIgnore, spanIdOnIgnore) = CurrentTraceIds();
            _logger.LogInformation(
                "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reservation_expired safely ignored (correlation {CorrelationId}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                saga.Id,
                orderId.Value,
                saga.State,
                correlationId,
                OrderSagaTransitionOutcome.Ignored,
                traceIdOnIgnore,
                spanIdOnIgnore);

            return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Ignored, saga.Id);
        }

        // Valid expiry: WaitingForPayment → Cancelled; cancel the Order through its own domain transition.
        var order = await _orderStore
            .GetByIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            throw new InvalidOperationException(
                $"Cannot cancel order '{orderId.Value}': the order does not exist.");
        }

        var cancel = order.Cancel(occurredAtUtc);

        if (cancel.IsFailure)
        {
            // Pending is the only Order state that allows cancellation, so a failed Cancel means the
            // Order is already Cancelled — the desired end state is already reached.
            _logger.LogInformation(
                "Order {OrderId} was already cancelled when applying inventory.reservation_expired; keeping the cancelled state.",
                orderId.Value);
        }

        expiryActivity?
            .AddTag("saga.new.state", saga.State.ToString())
            .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

        var (traceId, spanId) = CurrentTraceIds();
        _logger.LogInformation(
            "Applied inventory.reservation_expired to saga {SagaId} for order {OrderId} (reservation {ReservationId}, correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
            saga.Id,
            orderId.Value,
            reservationId,
            correlationId,
            causationId,
            previousState,
            saga.State,
            OrderSagaTransitionOutcome.Applied,
            traceId,
            spanId,
            order.Status);

        return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
    }

    /// <summary>
    /// Applies an <c>inventory.reservation_failed</c> result to the saga (Phase 8.6):
    /// <c>WaitingForInventory → Failed</c> and the Order transitions through its own <c>Cancel</c>
    /// (Pending → Cancelled), so the failure is reflected in the Order domain model — never bypassed.
    /// If the Order is already Cancelled (e.g. a concurrent customer cancellation) the desired end state
    /// is already reached and the saga failure is still applied. A result for an already-failed saga is
    /// an idempotent no-op; a result for a completed saga is a conflicting rejection; a result for a
    /// non-terminal saga is an acknowledged anomaly. A result for an order with no saga/order throws
    /// (anomaly → retry/dead-letter). The saga transition and the Order cancellation are staged together
    /// for the caller's unit of work, so they commit atomically with the processed-message record.
    /// </summary>
    /// <param name="orderId">The order whose reservation failed.</param>
    /// <param name="correlationId">The correlation id carried by the consumed result message.</param>
    /// <param name="causationId">The consumed result message's event id.</param>
    /// <param name="reason">The stable Inventory error code that caused the failure.</param>
    /// <param name="occurredAtUtc">The moment the reservation failed (from the consumed message).</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    public async Task<OrderSagaTransitionResult> ApplyReservationFailureAsync(
        OrderId orderId,
        Guid correlationId,
        Guid? causationId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (occurredAtUtc == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(occurredAtUtc));

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
        {
            throw new InvalidOperationException(
                $"Cannot apply a reservation failure for order '{orderId.Value}': no saga exists for the order.");
        }

        var previousState = saga.State;
        using var failureActivity = StartSagaActivity(
            "Orders.Saga.ApplyReservationFailure",
            orderId,
            saga.Id,
            previousState,
            correlationId: correlationId,
            causationId: causationId);

        switch (saga.State)
        {
            case OrderSagaState.WaitingForInventory:
            {
                var transition = saga.Fail(occurredAtUtc);

                if (transition.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Saga for order '{orderId.Value}' could not fail: {transition.Error.Code}.");
                }

                var order = await _orderStore
                    .GetByIdAsync(orderId, cancellationToken)
                    .ConfigureAwait(false);

                if (order is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot cancel order '{orderId.Value}': the order does not exist.");
                }

                var cancel = order.Cancel(occurredAtUtc);

                if (cancel.IsFailure)
                {
                    // Pending is the only Order state that allows cancellation, so a failed Cancel
                    // means the Order is already Cancelled — the desired end state is already reached.
                    _logger.LogInformation(
                        "Order {OrderId} was already cancelled when applying inventory.reservation_failed; keeping the cancelled state.",
                        orderId.Value);
                }

                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Applied.ToString());

                var (traceId, spanId) = CurrentTraceIds();
                _logger.LogInformation(
                    "Applied inventory.reservation_failed to saga {SagaId} for order {OrderId} (reason '{Reason}', correlation {CorrelationId}, causation {CausationId}): state {PreviousState} → {NewState} (outcome {Outcome}, trace {TraceId}, span {SpanId}); order is {OrderStatus}.",
                    saga.Id,
                    orderId.Value,
                    reason,
                    correlationId,
                    causationId,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Applied,
                    traceId,
                    spanId,
                    order.Status);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Applied, saga.Id);
            }
            case OrderSagaState.Failed:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Duplicate.ToString());

                var (traceIdOnFailed, spanIdOnFailed) = CurrentTraceIds();
                _logger.LogInformation(
                    "Saga {SagaId} for order {OrderId} is already failed; duplicate inventory.reservation_failed acknowledged (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Duplicate,
                    traceIdOnFailed,
                    spanIdOnFailed);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Duplicate, saga.Id);
            case OrderSagaState.Completed:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Conflicting.ToString());

                var (traceIdOnCompleted, spanIdOnCompleted) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is already completed; conflicting inventory.reservation_failed rejected (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Conflicting,
                    traceIdOnCompleted,
                    spanIdOnCompleted);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Conflicting, saga.Id);
            default:
                failureActivity?
                    .AddTag("saga.new.state", saga.State.ToString())
                    .AddTag("saga.outcome", OrderSagaTransitionOutcome.Invalid.ToString());

                var (traceIdOnInvalid, spanIdOnInvalid) = CurrentTraceIds();
                _logger.LogWarning(
                    "Saga {SagaId} for order {OrderId} is in state {SagaState}; inventory.reservation_failed cannot be applied (previous state {PreviousState}, new state {NewState}, outcome {Outcome}, trace {TraceId}, span {SpanId}).",
                    saga.Id,
                    orderId.Value,
                    saga.State,
                    previousState,
                    saga.State,
                    OrderSagaTransitionOutcome.Invalid,
                    traceIdOnInvalid,
                    spanIdOnInvalid);

                return new OrderSagaTransitionResult(OrderSagaTransitionOutcome.Invalid, saga.Id);
        }
    }
}
