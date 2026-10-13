using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Aggregate-side Orders persistence port: tracked aggregate loads and the unit
/// of work. Used by lifecycle commands, the saga orchestrator, and the summary
/// projection (all mutate or stage through the tracked aggregate).
/// </summary>
public interface IOrderAggregateStore
{
    Task<Order?> GetOwnedOrderAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
