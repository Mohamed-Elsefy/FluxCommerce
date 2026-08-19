using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Phase 8.2 persistence port for the Order/Inventory Saga state (ADR-009). The saga is the only
/// durable progress record of the workflow, so it is identified both by its Order (one saga per order,
/// enforced by a unique index) and by the correlation id of the messages it publishes and consumes.
/// Stores only stage changes inside the caller's unit of work; the caller owns the commit, which keeps
/// saga updates atomic with the module idempotency boundary (Phase 8.3).
/// </summary>
public interface IOrderSagaStore
{
    Task<OrderSaga?> GetByOrderIdAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task<OrderSaga?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default);

    void Add(OrderSaga saga);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}