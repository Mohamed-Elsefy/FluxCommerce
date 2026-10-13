using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Phase 7.9 <see cref="IOrderSummaryProjectionService"/>: loads the Order aggregate through
/// <see cref="IOrderAggregateStore.GetByIdAsync"/> and projects its current state into an
/// <see cref="OrderSummaryProjection"/>. A missing order is a retryable failure (the delivery ordering
/// with the business commit is at-least-once, so the source order can legitimately not exist yet on
/// the first attempt). The service stages the row via <see cref="IOrderSummaryProjectionStore"/> and
/// never commits: the idempotent-processing boundary does that atomically with the processed-message
/// record.
/// </summary>
public sealed class OrderSummaryProjectionService : IOrderSummaryProjectionService
{
    private readonly IOrderAggregateStore _orderStore;
    private readonly IOrderSummaryProjectionStore _projectionStore;

    public OrderSummaryProjectionService(
        IOrderAggregateStore orderStore,
        IOrderSummaryProjectionStore projectionStore)
    {
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
        _projectionStore = projectionStore ?? throw new ArgumentNullException(nameof(projectionStore));
    }

    public async Task ProjectAsync(
        Guid orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderStore
            .GetByIdAsync(new OrderId(orderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            throw new InvalidOperationException(
                $"The OrderSummary projection could not be built because order '{orderId}' does not exist yet.");
        }

        _projectionStore.Add(new OrderSummaryProjection(
            order.Id.Value,
            order.OrderNumber.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.GrandTotal.Amount,
            order.GrandTotal.Currency,
            order.LineItems.Count,
            correlationId,
            causationId,
            order.CreatedAtUtc,
            DateTimeOffset.UtcNow));
    }
}