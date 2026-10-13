using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// <see cref="IOrderRefundService"/> implemented by driving the Orders-owned saga
/// orchestrator. The saga transition and the staged refund outbox record commit atomically
/// through the module store. The refund keeps the saga's correlation id so the
/// <c>Orders → Payments → Orders → Inventory</c> compensation chain stays traceable; the
/// admin action itself is a new chain link (no incoming causation).
/// </summary>
public sealed class OrderRefundService : IOrderRefundService
{
    private readonly IOrderSagaStore _sagaStore;
    private readonly IOrderAggregateStore _orderStore;
    private readonly OrderSagaOrchestrator _orchestrator;

    public OrderRefundService(
        IOrderSagaStore sagaStore,
        IOrderAggregateStore orderStore,
        OrderSagaOrchestrator orchestrator)
    {
        _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<RefundRequestResult> RequestRefundAsync(
        OrderId orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));

        var order = await _orderStore
            .GetByIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
            return new RefundRequestResult(RefundRequestOutcome.OrderNotFound, null);

        var saga = await _sagaStore
            .GetByOrderIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);

        if (saga is null)
            return new RefundRequestResult(RefundRequestOutcome.NotEligible, order);

        OrderSagaTransitionResult transition;
        try
        {
            transition = await _orchestrator
                .RequestRefundAsync(orderId, reason.Trim(), saga.CorrelationId, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Missing payment correlation or a failed transition: the order cannot be refunded
            // through the saga from its current state.
            return new RefundRequestResult(RefundRequestOutcome.NotEligible, order);
        }

        var outcome = transition.Outcome switch
        {
            OrderSagaTransitionOutcome.Applied => RefundRequestOutcome.Applied,
            OrderSagaTransitionOutcome.Duplicate => RefundRequestOutcome.Duplicate,
            _ => RefundRequestOutcome.NotEligible,
        };

        if (outcome is RefundRequestOutcome.Applied or RefundRequestOutcome.Duplicate)
            await _orderStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefundRequestResult(outcome, order);
    }
}
