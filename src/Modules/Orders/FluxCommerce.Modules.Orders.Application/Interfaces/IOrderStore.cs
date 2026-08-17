using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence driven port for the customer-facing Orders queries and lifecycle commands. List
/// queries are served from a lightweight projection; single-order reads and cancellations load the
/// tracked aggregate (so a cancellation can mutate and flush the Order). Ownership is always enforced
/// by filtering on the current customer, so an Order can never be observed or cancelled by another
/// user. <see cref="GetByIdAsync"/> is the integration-event projection read: it loads the tracked
/// aggregate by identity alone (no customer scoping) because the Order Summary projection consumer
/// (Phase 7.9) reacts to events emitted by the same module.
/// </summary>
public interface IOrderStore
{
    Task<PagedResult<OrderSummaryReadModel>> GetMyOrdersPageAsync(
        CustomerId customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Order?> GetOwnedOrderAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
