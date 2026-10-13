namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence driven port for the customer-facing Orders queries and lifecycle commands. List
/// queries are served from a lightweight projection; single-order reads and cancellations load the
/// tracked aggregate (so a cancellation can mutate and flush the Order). Ownership is always enforced
/// by filtering on the current customer, so an Order can never be observed or cancelled by another
/// user. <see cref="IOrderAggregateStore.GetByIdAsync"/> is the integration-event projection read:
/// it loads the tracked aggregate by identity alone (no customer scoping) because the Order Summary
/// projection consumer (Phase 7.9) reacts to events emitted by the same module.
///
/// Kept as the composite of <see cref="IOrderAggregateStore"/> (tracked aggregates + unit of work)
/// and <see cref="IOrderQueryStore"/> (read-only projections) so existing consumers keep compiling;
/// new consumers should depend on the narrow port they actually need.
/// </summary>
public interface IOrderStore : IOrderAggregateStore, IOrderQueryStore
{
}
